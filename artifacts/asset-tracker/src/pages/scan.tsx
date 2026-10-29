import { useEffect, useRef, useState } from "react";
import { Link } from "wouter";
import { getScanCodeQueryKey, useScanCode } from "@workspace/api-client-react";
import { CheckCircle2, ScanLine, XCircle } from "lucide-react";
import { Layout } from "@/components/layout";
import { SbuBadge } from "@/components/sbu-badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { currentToken } from "@/hooks/use-auth";
import { apiErrorMessage } from "@/lib/api-error";
import { STATUS_LABEL } from "@/lib/finance";

/** The asset photo, fetched with the session's token. */
function Photo({ id }: { id: number }) {
  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    let objectUrl: string | null = null;
    const token = currentToken();
    fetch(`/api/attachments/${id}?inline=true`, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
      .then((r) => (r.ok ? r.blob() : null))
      .then((b) => { if (b) { objectUrl = URL.createObjectURL(b); setUrl(objectUrl); } })
      .catch(() => setUrl(null));
    return () => { if (objectUrl) URL.revokeObjectURL(objectUrl); };
  }, [id]);
  return url ? <img src={url} alt="Asset" className="h-32 w-32 rounded-md border object-cover" /> : null;
}

/**
 * Scan a tag, asset number or serial (SG-01, SI-03). The Gate Officer sees assets only through this screen;
 * another SBU's asset shows its basic identification only. A scanner typing into the field and pressing Enter works.
 */
export default function Scan() {
  const [text, setText] = useState("");
  const [code, setCode] = useState("");
  const input = useRef<HTMLInputElement>(null);
  const { data, error, isFetching } = useScanCode({ code }, { query: { enabled: code.length > 0, queryKey: getScanCodeQueryKey({ code }), retry: false } });
  useEffect(() => input.current?.focus(), []);

  const cleared = data?.gateClearance === "approved";
  return (
    <Layout>
      <div className="mx-auto max-w-xl space-y-5">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Scan</h1>
          <p className="text-sm text-muted-foreground">Tag, asset number or serial. Results appear in seconds; nothing here changes the record.</p>
        </div>
        <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); setCode(text.trim()); }}>
          <Input ref={input} value={text} onChange={(e) => setText(e.target.value)} placeholder="Scan or type a code" aria-label="Code" className="font-mono" />
          <Button type="submit" disabled={!text.trim() || isFetching}><ScanLine className="mr-1 h-4 w-4" /> Look up</Button>
        </form>
        {error && <p className="rounded-md border border-red-500/40 bg-red-500/10 p-3 text-sm">{apiErrorMessage(error)}</p>}
        {data && (
          <section className="space-y-4 rounded-md border bg-card p-4">
            {data.gateClearance !== "not_shown" && (
              <div className={cleared ? "flex items-center gap-3 rounded-md bg-green-600 p-4 text-white" : "flex items-center gap-3 rounded-md bg-red-600 p-4 text-white"}>
                {cleared ? <CheckCircle2 className="h-8 w-8" /> : <XCircle className="h-8 w-8" />}
                <div>
                  <div className="text-lg font-bold">{cleared ? "Approved to leave" : "Not approved to leave"}</div>
                  <div className="text-sm opacity-90">
                    {data.gatePassId ? `Gate pass #${data.gatePassId} · ${data.gatePassStatus}${data.gatePassReturnDate ? ` · return by ${data.gatePassReturnDate}` : ""}` : "No gate pass"}
                  </div>
                </div>
              </div>
            )}
            <div className="flex gap-4">
              {data.photoAttachmentId && <Photo id={data.photoAttachmentId} />}
              <div className="min-w-0 flex-1 space-y-1 text-sm">
                <div className="text-base font-semibold">{data.description}</div>
                <div className="font-mono text-xs text-muted-foreground">{data.assetNumber ?? "number pending"} · {data.rfidTag ?? "untagged"}</div>
                <div className="flex items-center gap-2">{data.sbuCode && <SbuBadge code={data.sbuCode} />}<span>{STATUS_LABEL[data.status] ?? data.status}</span></div>
                {data.holder && <div>Holder: <span className="font-medium">{data.holder}</span></div>}
                {data.gateClearance === "not_shown" && <p className="text-xs text-muted-foreground">Another SBU's asset: basic identification only.</p>}
              </div>
            </div>
            {data.canOpen && <Button variant="outline" size="sm" asChild><Link href={`/assets/${data.assetId}/card`}>Open asset card</Link></Button>}
          </section>
        )}
      </div>
    </Layout>
  );
}
