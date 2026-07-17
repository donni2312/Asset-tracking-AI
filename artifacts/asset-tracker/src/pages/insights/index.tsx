import { Layout } from "@/components/layout";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useListInsights, useDismissInsight, useAiQuery } from "@workspace/api-client-react";
import { BrainCircuit, Send, AlertTriangle, Info, Bot, Check } from "lucide-react";
import { useState, useRef, useEffect } from "react";
import { formatDate } from "@/lib/utils";

export default function Insights() {
  const { data: insights, refetch: refetchInsights } = useListInsights();
  const dismissInsight = useDismissInsight();
  const askAi = useAiQuery();
  
  const [question, setQuestion] = useState("");
  const [chat, setChat] = useState<{role: 'user' | 'ai', content: string, timestamp: string}[]>([
    { role: 'ai', content: "Hello. I am AssetIQ AI. You can ask me questions about your asset inventory, movements, or anomalies.", timestamp: new Date().toISOString() }
  ]);
  const chatEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [chat]);

  const activeInsights = insights?.filter(i => !i.dismissed) || [];

  const handleDismiss = (id: number) => {
    dismissInsight.mutate({ id }, {
      onSuccess: () => refetchInsights()
    });
  };

  const handleAsk = (e: React.FormEvent) => {
    e.preventDefault();
    if (!question.trim()) return;

    const currentQ = question;
    setQuestion("");
    setChat(prev => [...prev, { role: 'user', content: currentQ, timestamp: new Date().toISOString() }]);

    askAi.mutate({ data: { question: currentQ } }, {
      onSuccess: (res) => {
        setChat(prev => [...prev, { role: 'ai', content: res.answer, timestamp: res.timestamp }]);
      },
      onError: () => {
        setChat(prev => [...prev, { role: 'ai', content: "I encountered an error querying the system. Please try again.", timestamp: new Date().toISOString() }]);
      }
    });
  };

  return (
    <Layout>
      <div className="space-y-6 h-full flex flex-col max-w-5xl mx-auto">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">AI Insights & Analytics</h1>
          <p className="text-muted-foreground text-sm">System-detected anomalies and natural language querying.</p>
        </div>

        {/* Active Insights */}
        {activeInsights.length > 0 && (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 shrink-0">
            {activeInsights.map(insight => (
              <div key={insight.id} className={`border rounded-lg p-4 flex gap-4 ${
                insight.severity === 'critical' ? 'border-destructive/50 bg-destructive/5' :
                insight.severity === 'warning' ? 'border-amber-500/50 bg-amber-500/5' :
                'border-blue-500/50 bg-blue-500/5'
              }`}>
                <div className="shrink-0 mt-0.5">
                  {insight.severity === 'critical' ? <AlertTriangle className="w-5 h-5 text-destructive" /> :
                   insight.severity === 'warning' ? <AlertTriangle className="w-5 h-5 text-amber-500" /> :
                   <Info className="w-5 h-5 text-blue-500" />}
                </div>
                <div className="flex-1 space-y-1">
                  <div className="flex justify-between items-start">
                    <p className="text-sm font-medium leading-tight">{insight.message}</p>
                    <Button variant="ghost" size="icon" className="h-6 w-6 -mt-1 -mr-1" onClick={() => handleDismiss(insight.id)}>
                      <Check className="w-4 h-4 text-muted-foreground" />
                    </Button>
                  </div>
                  {insight.assetName && (
                    <p className="text-xs text-muted-foreground font-mono">Asset: {insight.assetName}</p>
                  )}
                  <p className="text-[10px] text-muted-foreground">{formatDate(insight.createdAt)}</p>
                </div>
              </div>
            ))}
          </div>
        )}

        {/* Chat Interface */}
        <Card className="flex-1 flex flex-col min-h-[400px]">
          <CardHeader className="border-b shrink-0 py-4">
            <CardTitle className="text-base flex items-center gap-2">
              <BrainCircuit className="w-5 h-5 text-primary" />
              Natural Language Assistant
            </CardTitle>
          </CardHeader>
          <CardContent className="flex-1 p-0 flex flex-col relative bg-muted/10">
            <div className="flex-1 overflow-y-auto p-4 space-y-4">
              {chat.map((msg, i) => (
                <div key={i} className={`flex gap-3 max-w-[85%] ${msg.role === 'user' ? 'ml-auto flex-row-reverse' : ''}`}>
                  <div className={`w-8 h-8 rounded-full shrink-0 flex items-center justify-center ${msg.role === 'user' ? 'bg-primary text-primary-foreground' : 'bg-secondary text-secondary-foreground border'}`}>
                    {msg.role === 'user' ? <span className="text-xs font-medium">U</span> : <Bot className="w-4 h-4" />}
                  </div>
                  <div className={`p-3 rounded-lg text-sm ${msg.role === 'user' ? 'bg-primary text-primary-foreground' : 'bg-background border shadow-sm'}`}>
                    <div className="whitespace-pre-wrap">{msg.content}</div>
                    <div className={`text-[10px] mt-1 ${msg.role === 'user' ? 'text-primary-foreground/70 text-right' : 'text-muted-foreground'}`}>
                      {new Date(msg.timestamp).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}
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
                    <span className="w-1.5 h-1.5 bg-primary/50 rounded-full animate-bounce" style={{animationDelay: '0.2s'}}></span>
                    <span className="w-1.5 h-1.5 bg-primary/50 rounded-full animate-bounce" style={{animationDelay: '0.4s'}}></span>
                  </div>
                </div>
              )}
              <div ref={chatEndRef} />
            </div>
            <div className="p-4 border-t bg-background shrink-0">
              <form onSubmit={handleAsk} className="flex gap-2 relative">
                <Input 
                  value={question} 
                  onChange={e => setQuestion(e.target.value)} 
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
