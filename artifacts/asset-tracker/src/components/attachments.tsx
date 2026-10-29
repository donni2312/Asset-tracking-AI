import { useEffect, useRef, useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { getListAssetAttachmentsQueryKey, useListAssetAttachments, type Attachment } from "@workspace/api-client-react";
import { Download, Eye, FileText, Paperclip } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { useToast } from "@/hooks/use-toast";
import { currentToken } from "@/hooks/use-auth";
import { SELECT_CLASS } from "@/lib/constants";
import { saveBlob } from "@/lib/finance";

const KIND_LABEL: Record<string, string> = { invoice: "Invoice", photo: "Photo", other: "Document" };
const MAX_BYTES = 10 * 1024 * 1024;

/** The file behind an attachment, fetched with the session's token (an <img src> cannot send one). */
async function fetchFile(id: number, inline: boolean): Promise<Blob> {
  const token = currentToken();
  const res = await fetch(`/api/attachments/${id}${inline ? "?inline=true" : ""}`, { headers: token ? { Authorization: `Bearer ${token}` } : {} });
  if (!res.ok) throw new Error(`The file could not be loaded (${res.status})`);
  return res.blob();
}

function Thumbnail({ attachment }: { attachment: Attachment }) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    let revoked = false;
    let objectUrl: string | null = null;
    fetchFile(attachment.id, true)
      .then((blob) => {
        if (revoked) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => setUrl(null));
    return () => {
      revoked = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [attachment.id]);
  return url
    ? <img src={url} alt={attachment.fileName} className="h-16 w-16 flex-shrink-0 rounded border object-cover" />
    : <div className="h-16 w-16 flex-shrink-0 rounded border bg-muted" />;
}

/**
 * Invoices, photos and documents on the asset (AU-01, AU-04, AU-07): shown and downloaded here, uploaded by
 * whoever may add them (the API decides). Attachments are kept for the audit and cannot be removed.
 */
export function AttachmentsPanel({ assetId, canAttach }: { assetId: number; canAttach: boolean }) {
  const { data: attachments } = useListAssetAttachments(assetId, { query: { queryKey: getListAssetAttachmentsQueryKey(assetId) } });
  const qc = useQueryClient();
  const { toast } = useToast();
  const fileInput = useRef<HTMLInputElement>(null);
  const [kind, setKind] = useState("invoice");
  const [uploading, setUploading] = useState(false);

  async function upload(file: File) {
    if (file.size > MAX_BYTES) {
      toast({ title: "File too large", description: "Attachments can be up to 10 MB.", variant: "destructive" });
      return;
    }
    const form = new FormData();
    form.append("file", file);
    form.append("kind", kind);
    setUploading(true);
    try {
      const token = currentToken();
      const res = await fetch(`/api/assets/${assetId}/attachments`, { method: "POST", body: form, headers: token ? { Authorization: `Bearer ${token}` } : {} });
      if (!res.ok) {
        const body = await res.json().catch(() => null);
        throw new Error(body?.error ?? `Upload failed (${res.status})`);
      }
      toast({ title: "Attached", description: file.name });
      qc.invalidateQueries({ queryKey: getListAssetAttachmentsQueryKey(assetId) });
    } catch (e) {
      toast({ title: "Could not attach the file", description: e instanceof Error ? e.message : String(e), variant: "destructive" });
    } finally {
      setUploading(false);
      if (fileInput.current) fileInput.current.value = "";
    }
  }

  async function open(a: Attachment, inline: boolean) {
    try {
      const blob = await fetchFile(a.id, inline);
      if (!inline) return saveBlob(blob, a.fileName);
      const url = URL.createObjectURL(blob);
      window.open(url, "_blank", "noopener");
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
    } catch (e) {
      toast({ title: "Could not open the file", description: e instanceof Error ? e.message : String(e), variant: "destructive" });
    }
  }

  return (
    <section className="rounded-md border bg-card p-4">
      <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">Attachments</h2>
          <p className="text-xs text-muted-foreground">Invoices, photos and documents. Kept for the audit; they cannot be removed.</p>
        </div>
        {canAttach && (
          <div className="flex flex-wrap items-end gap-2">
            <div className="space-y-1">
              <Label className="text-xs" htmlFor="attachment-kind">Type</Label>
              <select id="attachment-kind" className={SELECT_CLASS} value={kind} onChange={(e) => setKind(e.target.value)}>
                <option value="invoice">Invoice</option>
                <option value="photo">Photo</option>
                <option value="other">Other document</option>
              </select>
            </div>
            <input ref={fileInput} type="file" accept="application/pdf,image/jpeg,image/png" className="hidden"
              onChange={(e) => e.target.files?.[0] && upload(e.target.files[0])} />
            <Button size="sm" variant="outline" disabled={uploading} onClick={() => fileInput.current?.click()}>
              <Paperclip className="mr-1 h-4 w-4" /> {uploading ? "Uploading…" : "Attach file"}
            </Button>
          </div>
        )}
      </div>
      <ul className="space-y-2">
        {(attachments ?? []).map((a) => (
          <li key={a.id} className="flex items-center gap-3 rounded border border-border/60 p-2">
            {a.kind === "photo" ? <Thumbnail attachment={a} /> : <FileText className="h-8 w-8 flex-shrink-0 text-muted-foreground" />}
            <div className="min-w-0 flex-1">
              <div className="truncate text-sm font-medium">{a.fileName}</div>
              <div className="text-xs text-muted-foreground">
                {KIND_LABEL[a.kind] ?? a.kind}{a.upgradeId ? ` · upgrade #${a.upgradeId}` : ""} · {(a.sizeBytes / 1024).toFixed(0)} KB · {a.uploadedBy} · {new Date(a.uploadedAt).toLocaleDateString()}
              </div>
            </div>
            <Button size="icon" variant="ghost" title="View" aria-label={`View ${a.fileName}`} onClick={() => open(a, true)}><Eye className="h-4 w-4" /></Button>
            <Button size="icon" variant="ghost" title="Download" aria-label={`Download ${a.fileName}`} onClick={() => open(a, false)}><Download className="h-4 w-4" /></Button>
          </li>
        ))}
        {attachments?.length === 0 && <li className="text-sm text-muted-foreground">No attachments yet.</li>}
      </ul>
    </section>
  );
}
