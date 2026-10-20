import { useMemo } from "react";
import { useListAssets, useListGatePasses, useListMovements } from "@workspace/api-client-react";

/**
 * The people known to the system.
 *
 * There is no staff directory in the MVP — no users table and no authentication
 * (see docs/project-plan.md §7). People exist only as names recorded on records:
 * `assets.assignedTo`, `gate_passes.requestedBy / approvedBy` and
 * `movements.movedBy`. This gathers those into one de-duplicated list so a name
 * can be picked instead of retyped (and spelled three different ways).
 *
 * Matching is case-insensitive; the spelling kept is the one that appears most
 * often, so a stray "tendai moyo" does not replace "Tendai Moyo".
 */
export interface Person {
  /** Name as it will be written to the database. */
  name: string;
  /** Assets currently assigned to them (0 if they only appear on past records). */
  assetCount: number;
  /** Where the name was found, for the hint line in the picker. */
  roles: string[];
}

function add(
  index: Map<string, { spellings: Map<string, number>; assetCount: number; roles: Set<string> }>,
  raw: string | null | undefined,
  role: string,
  isAssignment = false,
) {
  const name = raw?.trim();
  if (!name) return;
  const key = name.toLowerCase();
  const entry = index.get(key) ?? { spellings: new Map(), assetCount: 0, roles: new Set<string>() };
  entry.spellings.set(name, (entry.spellings.get(name) ?? 0) + 1);
  entry.roles.add(role);
  if (isAssignment) entry.assetCount += 1;
  index.set(key, entry);
}

export function usePeople(): { people: Person[]; isLoading: boolean } {
  const { data: assets, isLoading: loadingAssets } = useListAssets();
  const { data: gatePasses, isLoading: loadingPasses } = useListGatePasses();
  const { data: movements, isLoading: loadingMovements } = useListMovements();

  const people = useMemo(() => {
    const index = new Map<string, { spellings: Map<string, number>; assetCount: number; roles: Set<string> }>();

    for (const asset of assets ?? []) add(index, asset.assignedTo, "holds an asset", true);
    for (const pass of gatePasses ?? []) {
      add(index, pass.requestedBy, "requested a gate pass");
      add(index, pass.approvedBy, "decided a gate pass");
    }
    for (const movement of movements ?? []) add(index, movement.movedBy, "recorded a movement");

    return [...index.values()]
      .map(({ spellings, assetCount, roles }) => {
        const name = [...spellings.entries()].sort((a, b) => b[1] - a[1])[0][0];
        return { name, assetCount, roles: [...roles] };
      })
      .sort((a, b) => a.name.localeCompare(b.name));
  }, [assets, gatePasses, movements]);

  return { people, isLoading: loadingAssets || loadingPasses || loadingMovements };
}

/** One-line description of where a person appears, for the picker's hint row. */
export function personHint(person: Person): string {
  const assets =
    person.assetCount === 0 ? null : `${person.assetCount} asset${person.assetCount === 1 ? "" : "s"} assigned`;
  return [assets, ...person.roles.filter((r) => r !== "holds an asset")].filter(Boolean).join(" · ");
}
