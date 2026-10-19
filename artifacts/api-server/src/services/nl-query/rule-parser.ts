import { categoryFromWord, type GatePassStatusFilter, type Intent, type Period } from "./intent";

/**
 * Deterministic, rule-based question parser. It always works (no network, no
 * API key) and is the fallback whenever the OpenAI parser is disabled or fails.
 *
 * Covers the plan's three example questions (section 6.5):
 *   "Where is the Dell laptop assigned to John Doe?"   → locate_asset
 *   "Which assets have not moved in 30 days?"           → not_moved
 *   "Show me all gate passes approved this week"        → gate_passes
 */

const STOPWORDS = new Set([
  "where", "is", "are", "was", "the", "a", "an", "my", "our", "his", "her", "their", "currently", "current",
  "located", "location", "locate", "find", "show", "me", "please", "can", "could", "you", "tell", "which", "what",
  "of", "for", "now", "right", "at", "in", "on", "to", "be", "does", "do", "whereabouts", "asset", "assets", "item",
  "items", "device", "devices", "i", "we", "it", "its", "that", "this", "there", "here", "and", "or", "with", "from",
  "by", "about", "any", "all", "whose", "has", "have", "been", "kept", "sitting", "lying", "gone",
]);

/** Words that end a person's name captured after "assigned to …". */
const NAME_TERMINATORS = new Set([
  "in", "at", "on", "this", "that", "currently", "now", "today", "right", "please", "and", "or", "is", "was",
  "who", "which", "with", "from", "for", "located", "kept",
]);

const DEVICE_WORDS = new Set(["asset", "device", "computer", "machine", "equipment", "kit"]);

export function normaliseQuestion(question: string): string {
  return question
    .toLowerCase()
    .replace(/[‘’]/g, "'")
    .replace(/[“”"?!.,;:()[\]]/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

/** "assigned to John Doe", "held by …", or a possessive such as "John Doe's laptop". */
export function extractAssignee(q: string): { assignee: string | null; rest: string } {
  const m = q.match(/\b(?:assigned to|issued to|allocated to|held by|belonging to|belongs to|used by)\s+([a-z][a-z'-]*(?:\s+[a-z][a-z'-]*){0,3})/);
  if (m) {
    const name: string[] = [];
    for (const word of m[1].split(" ")) {
      if (NAME_TERMINATORS.has(word)) break;
      name.push(word);
    }
    if (name.length > 0) return { assignee: name.join(" "), rest: q.replace(m[0], " ") };
  }

  // Possessive: "<first> <last>'s <device>". Scan tokens so "where's john doe's laptop" works
  // (a single regex match would stop at "where's john").
  const tokens = q.split(" ");
  for (let i = 0; i < tokens.length - 1; i++) {
    const next = tokens[i + 1];
    if (!tokens[i].endsWith("'s") || !(categoryFromWord(next) || DEVICE_WORDS.has(next))) continue;
    const surname = tokens[i].slice(0, -2);
    const previous = i > 0 ? tokens[i - 1] : "";
    const nameWords = [previous, surname].filter((w) => /^[a-z][a-z-]*$/.test(w) && !STOPWORDS.has(w));
    if (nameWords.length === 0) continue;
    const start = nameWords.length === 2 ? i - 1 : i;
    const rest = [...tokens.slice(0, start), ...tokens.slice(i + 1)].join(" ");
    return { assignee: nameWords.join(" "), rest };
  }
  return { assignee: null, rest: q };
}

/** Significant words left after removing stop words (brand, model, category…). */
export function extractTerms(text: string): string[] {
  return text
    .split(" ")
    .map((w) => w.replace(/'s$/, "").replace(/^'+|'+$/g, ""))
    .filter((w) => w.length > 1 && !STOPWORDS.has(w));
}

/** "30 days", "4 weeks", "2 months", "a week" → days. */
export function parseDays(q: string, fallback = 30): number {
  const m = q.match(/\b(\d{1,4})\s*(day|days|week|weeks|month|months)\b/);
  if (m) {
    const n = Number(m[1]);
    const factor = m[2].startsWith("week") ? 7 : m[2].startsWith("month") ? 30 : 1;
    return Math.max(1, n * factor);
  }
  if (/\b(?:a|one) fortnight\b|\btwo weeks\b/.test(q)) return 14;
  if (/\b(?:a|one) week\b/.test(q)) return 7;
  if (/\b(?:a|one) month\b/.test(q)) return 30;
  return fallback;
}

export function parsePeriod(q: string): { period: Period; periodDays: number | null } {
  if (/\btoday\b/.test(q)) return { period: "today", periodDays: null };
  if (/\byesterday\b/.test(q)) return { period: "yesterday", periodDays: null };
  if (/\b(?:this|the current) week\b/.test(q)) return { period: "this_week", periodDays: null };
  if (/\b(?:last|previous|past) week\b/.test(q)) return { period: "last_week", periodDays: null };
  if (/\bthis month\b/.test(q)) return { period: "this_month", periodDays: null };
  const lastN = q.match(/\b(?:last|past|previous)\s+(\d{1,3})\s+days?\b/);
  if (lastN) return { period: "last_n_days", periodDays: Math.max(1, Number(lastN[1])) };
  return { period: "all", periodDays: null };
}

function gatePassStatus(q: string): GatePassStatusFilter | null {
  if (/\bapproved\b|\bauthori[sz]ed\b/.test(q)) return "approved";
  if (/\brejected\b|\bdenied\b|\bdeclined\b/.test(q)) return "rejected";
  if (/\bpending\b|\bawaiting\b|\bwaiting\b|\boutstanding\b|\bunapproved\b/.test(q)) return "pending";
  return null;
}

const GATE_PASS = /\bgate[\s-]?pass(?:es)?\b|\bpasses\b/;
const NOT_MOVED =
  /\b(?:not|never|hasn't|haven't|hasnt|havent)\s+(?:been\s+)?(?:moved|used|seen|scanned|touched)\b|\bidle\b|\bstationary\b|\bunmoved\b|\bno movements?\b|\bnot moving\b/;
const LOCATE = /\bwhere\b|\blocate\b|\bfind\b|\bwhereabouts\b|\blocation of\b|\bwhere's\b/;
const UNASSIGNED = /\bunassigned\b|\bnot assigned\b|\bno one\b|\bnobody\b|\bwithout an? (?:owner|assignee)\b/;
const COUNT = /\bhow many\b|\btotal\b|\bcount\b|\bnumber of\b|\bsummary\b|\boverview\b/;

export function parseQuestion(question: string): Intent {
  const q = normaliseQuestion(question);
  if (!q) return { kind: "help" };

  if (GATE_PASS.test(q)) {
    return { kind: "gate_passes", status: gatePassStatus(q), ...parsePeriod(q) };
  }

  if (NOT_MOVED.test(q)) return { kind: "not_moved", days: parseDays(q) };

  if (UNASSIGNED.test(q)) return { kind: "unassigned" };

  const { assignee, rest } = extractAssignee(q);

  if (LOCATE.test(q)) {
    return { kind: "locate_asset", terms: extractTerms(rest), assignee };
  }

  if (assignee) return { kind: "assets_assigned_to", assignee };

  if (/\bmissing\b|\blost\b|\bstolen\b/.test(q)) return { kind: "assets_by_status", status: "missing" };
  if (/\bmaintenance\b|\brepairs?\b|\bbeing (?:fixed|repaired)\b/.test(q)) return { kind: "assets_by_status", status: "maintenance" };
  if (/\bretired\b|\bdisposed\b|\bdecommissioned\b/.test(q)) return { kind: "assets_by_status", status: "retired" };

  const category = q.split(" ").map(categoryFromWord).find((c): c is string => c !== null);
  if (category) return { kind: "category_count", category };

  if (COUNT.test(q)) return { kind: "count_summary" };

  return { kind: "help" };
}
