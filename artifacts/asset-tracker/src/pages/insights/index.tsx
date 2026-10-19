import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { useListInsights, useDismissInsight, useAiQuery, type AIQueryResult, type AIInsight } from "@workspace/api-client-react";
import { BrainCircuit, Send, AlertTriangle, Info, Bot, Check, ShieldAlert, Sparkles, Box } from "lucide-react";
import { useState, useRef, useEffect } from "react";
import { Link } from "wouter";
import { useQueryClient } from "@tanstack/react-query";
import { formatDate } from "@/lib/utils";

const EXAMPLES = [
  "Where is the Dell laptop assigned to John Doe?",
  "Which assets have not moved in 30 days?",
  "Show me all gate passes approved this week",
];

const TYPE_LABELS: Record<AIInsight["type"], string> = {
  anomaly: "Location mismatch / anomaly",
  idle_asset: "Idle asset",
  unauthorized_movement: "Unauthorised movement",
  missing_asset: "Missing asset",
  maintenance_due: "Maintenance due",
  unusual_pattern: "Out-of-hours / unusual pattern",
};

type ChatMessage = { role: "user" | "ai"; content: string; timestamp: string; result?: AIQueryResult };

function AnswerExtras({ result }: { result: AIQueryResult }) {
  const passes = result.relatedGatePasses ?? [];
  const assets = (result.relatedAssets ?? []).slice(0, 8);
  if (passes.length === 0 && assets.length === 0 && !result.source) return null;
  return (
    <div className="mt-2 space-y-2">
      {passes.length > 0 ? (
        <div className="flex flex-wrap gap-1.5">
          {passes.map((p) => (
            <Link key={p.id} href="/gate-passes" className="inline-flex items-center gap-1 rounded border px-1.5 py-0.5 text-[11px] hover:bg-muted">
              <ShieldAlert className="w-3 h-3" /> #{p.id} {p.assetName}
            </Link>
          ))}
        </div>
      ) : (
        assets.length > 0 && (
          <div className="flex flex-wrap gap-1.5">
            {assets.map((a) => (
              <Link key={a.id} href={`/assets/${a.id}`} className="inline-flex items-center gap-1 rounded border px-1.5 py-0.5 text-[11px] hover:bg-muted">
                <Box className="w-3 h-3" /> {a.name}
              </Link>
            ))}
          </div>
        )
      )}
      {result.source && (
        <div className="text-[10px] text-muted-foreground flex items-center gap-1">
          <Sparkles className="w-3 h-3" />
          Understood by {result.source === "openai" ? "OpenAI" : "the rule-based parser"}
          {result.intent ? ` · intent: ${result.intent.replace(/_/g, " ")}` : ""} · answer computed from live data
        </div>
      )}
    </div>
  );
}

export default function Insights() {
  const qc = useQueryClient();
  const { data: insights } = useListInsights();
  const dismissInsight = useDismissInsight();
  const askAi = useAiQuery();

  const [question, setQuestion] = useState("");
  const [chat, setChat] = useState<ChatMessage[]>([
    { role: "ai", content: "Hello. I am AssetIQ AI. Ask me about your asset inventory, movements, gate passes or anomalies.", timestamp: new Date().toISOString() },
  ]);
  const chatEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [chat]);

  const activeInsights = insights?.filter((i) => !i.dismissed) || [];

  const handleDismiss = (id: number) => {
    dismissInsight.mutate({ id }, { onSuccess: () => qc.invalidateQueries() });
  };

  const ask = (text: string) => {
    const q = text.trim();
    if (!q || askAi.isPending) return;
    setQuestion("");
    setChat((prev) => [...prev, { role: "user", content: q, timestamp: new Date().toISOString() }]);
    askAi.mutate(
      { data: { question: q } },
      {
        onSuccess: (res) => setChat((prev) => [...prev, { role: "ai", content: res.answer, timestamp: res.timestamp, result: res }]),
        onError: () =>
          setChat((prev) => [...prev, { role: "ai", content: "I encountered an error querying the system. Please try again.", timestamp: new Date().toISOString() }]),
      },
    );
  };

  return (
    <Layout>
      <div className="space-y-6 h-full flex flex-col max-w-5xl mx-auto">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">AI Insights & Analytics</h1>
        </div>

        {/* Active Insights */}
        {activeInsights.length > 0 && (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 shrink-0">
            {activeInsights.map((insight) => (
              <div
                key={insight.id}
                className={`border rounded-lg p-4 flex gap-4 ${
                  insight.severity === "critical" ? "border-destructive/50 bg-destructive/5" :
                  insight.severity === "warning" ? "border-amber-500/50 bg-amber-500/5" :
                  "border-blue-500/50 bg-blue-500/5"
                }`}
              >
                <div className="shrink-0 mt-0.5">
                  {insight.severity === "critical" ? <AlertTriangle className="w-5 h-5 text-destructive" /> :
                   insight.severity === "warning" ? <AlertTriangle className="w-5 h-5 text-amber-500" /> :
                   <Info className="w-5 h-5 text-blue-500" />}
                </div>
                <div className="flex-1 space-y-1">
                  <div className="flex justify-between items-start gap-2">
                    <div>
                      <div className="text-[10px] font-semibold uppercase tracking-wide text-muted-foreground mb-0.5">
                        {insight.severity} · {TYPE_LABELS[insight.type] ?? insight.type}
                      </div>
                      <p className="text-sm font-medium leading-tight">{insight.message}</p>
                    </div>
                    <Button variant="ghost" size="icon" className="h-6 w-6 -mt-1 -mr-1 shrink-0" onClick={() => handleDismiss(insight.id)} title="Dismiss">
                      <Check className="w-4 h-4 text-muted-foreground" />
                    </Button>
                  </div>
                  <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                    {insight.assetName && insight.assetId != null && (
                      <Link href={`/assets/${insight.assetId}`} className="font-mono hover:underline">Asset: {insight.assetName}</Link>
                    )}
                    {insight.sbuCode && <Badge variant="outline" className="text-[10px] py-0">{insight.sbuCode}</Badge>}
                    {insight.gatePassId != null && (
                      <Link href="/gate-passes" className="text-primary hover:underline">Review gate pass #{insight.gatePassId}</Link>
                    )}
                  </div>
                  <p className="text-[10px] text-muted-foreground">{formatDate(insight.createdAt)}</p>
                </div>
              </div>
            ))}
          </div>
        )}

        {/* Chat Interface */}
        <Card className="flex-1 flex flex-col min-h-[440px]">
          <CardHeader className="border-b shrink-0 py-4">
            <CardTitle className="text-base flex items-center gap-2">
              <BrainCircuit className="w-5 h-5 text-primary" />
              Natural Language Assistant
            </CardTitle>
          </CardHeader>
          <CardContent className="flex-1 p-0 flex flex-col relative bg-muted/10">
            <div className="flex-1 overflow-y-auto p-4 space-y-4">
              {chat.map((msg, i) => (
                <div key={i} className={`flex gap-3 max-w-[85%] ${msg.role === "user" ? "ml-auto flex-row-reverse" : ""}`}>
                  <div className={`w-8 h-8 rounded-full shrink-0 flex items-center justify-center ${msg.role === "user" ? "bg-primary text-primary-foreground" : "bg-secondary text-secondary-foreground border"}`}>
                    {msg.role === "user" ? <span className="text-xs font-medium">U</span> : <Bot className="w-4 h-4" />}
                  </div>
                  <div className={`p-3 rounded-lg text-sm ${msg.role === "user" ? "bg-primary text-primary-foreground" : "bg-background border shadow-sm"}`}>
                    <div className="whitespace-pre-wrap">{msg.content}</div>
                    {msg.result && <AnswerExtras result={msg.result} />}
                    <div className={`text-[10px] mt-1 ${msg.role === "user" ? "text-primary-foreground/70 text-right" : "text-muted-foreground"}`}>
                      {new Date(msg.timestamp).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })}
                    </div>
                  </div>
                </div>
              ))}
              {askAi.isPending && (
                <div className="flex gap-3 max-w-[85%]">
                  <div className="w-8 h-8 rounded-full bg-secondary text-secondary-foreground border flex items-center justify-center shrink-0">
                    <Bot className="w-4 h-4" />
                  </div>
                  <div className="p-3 rounded-lg bg-background border shadow-sm text-sm flex items-center gap-2">
                    <span className="w-1.5 h-1.5 bg-primary/50 rounded-full animate-bounce"></span>
                    <span className="w-1.5 h-1.5 bg-primary/50 rounded-full animate-bounce" style={{ animationDelay: "0.2s" }}></span>
                    <span className="w-1.5 h-1.5 bg-primary/50 rounded-full animate-bounce" style={{ animationDelay: "0.4s" }}></span>
                  </div>
                </div>
              )}
              <div ref={chatEndRef} />
            </div>
            <div className="p-4 border-t bg-background shrink-0 space-y-3">
              <div className="flex flex-wrap gap-2">
                {EXAMPLES.map((example) => (
                  <button
                    key={example}
                    type="button"
                    onClick={() => ask(example)}
                    disabled={askAi.isPending}
                    className="rounded-full border px-3 py-1 text-xs text-muted-foreground hover:bg-muted hover:text-foreground disabled:opacity-50"
                  >
                    {example}
                  </button>
                ))}
              </div>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  ask(question);
                }}
                className="flex gap-2 relative"
              >
                <Input
                  value={question}
                  onChange={(e) => setQuestion(e.target.value)}
                  placeholder="Ask about assets, e.g. 'Where is the missing projector?'"
                  className="pr-12 bg-muted/50"
                  disabled={askAi.isPending}
                />
                <Button type="submit" size="icon" className="absolute right-1 top-1 h-7 w-7" disabled={askAi.isPending || !question.trim()}>
                  <Send className="w-3.5 h-3.5" />
                </Button>
              </form>
            </div>
          </CardContent>
        </Card>
      </div>
    </Layout>
  );
}
