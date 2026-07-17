import { Layout } from "@/components/layout";
import { AlertCircle } from "lucide-react";
import { Link } from "wouter";
import { Button } from "@/components/ui/button";

export default function NotFound() {
  return (
    <Layout>
      <div className="flex flex-col items-center justify-center min-h-[60vh] space-y-4 text-center">
        <AlertCircle className="w-16 h-16 text-muted-foreground/50" />
        <div className="space-y-2">
          <h1 className="text-3xl font-bold tracking-tight">404 - Not Found</h1>
          <p className="text-muted-foreground">The page you're looking for doesn't exist or has been moved.</p>
        </div>
        <Button asChild className="mt-4">
          <Link href="/">Return to Dashboard</Link>
        </Button>
      </div>
    </Layout>
  );
}
