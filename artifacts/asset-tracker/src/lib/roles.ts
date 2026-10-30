import type { User, UserRole } from "@workspace/api-client-react";

/**
 * The roles of the Permissions matrix (Phase 2) and the role groups the API checks endpoint by endpoint
 * (Domain/Values.cs `Roles`). Keep the two in step. These only decide what the dashboard offers: the API
 * checks every action again, including the record-level rules (own SBU, IT / non-IT, own department).
 */
export const ROLE_NAME: Record<UserRole, string> = {
  group_finance: "Group Finance Executive",
  group_it: "Group IT Network & Hardware Admin",
  sbu_it: "SBU IT Admin / Technician",
  sbu_finance: "SBU Finance",
  gate_officer: "Gate Officer",
  hod: "Line Manager / HOD",
  sbu_hr: "SBU HR",
  stores: "Stores Clerk",
  sbu_admin: "SBU Admin Manager",
  auditor: "Auditor",
  employee: "Employee",
  system_admin: "System Administrator",
};

export const ROLE_SUMMARY: Record<UserRole, string> = {
  group_finance: "all SBUs, categories and depreciation policy",
  group_it: "IT assets of every SBU, user accounts",
  sbu_it: "IT assets of your SBU, its user accounts",
  sbu_finance: "cost, capitalisation and upgrades of your SBU",
  gate_officer: "scans and gate exits at your SBU",
  hod: "your department's equipment and gate passes",
  sbu_hr: "employees and toolboxes of your SBU",
  stores: "receiving, tagging and items in store",
  sbu_admin: "non-IT assets of your SBU",
  auditor: "read only, all SBUs, until your access expires",
  employee: "the items you hold",
  system_admin: "everything, every request logged",
};

const ALL: UserRole[] = ["group_finance", "group_it", "sbu_it", "sbu_finance", "gate_officer", "hod", "sbu_hr", "stores", "sbu_admin", "auditor", "employee", "system_admin"];
const ASSET_READERS: UserRole[] = ["group_finance", "group_it", "sbu_it", "sbu_finance", "hod", "sbu_hr", "stores", "sbu_admin", "auditor", "employee"];
const RECORD_READERS: UserRole[] = ASSET_READERS.filter((r) => r !== "employee");

export const ROLE_GROUPS = {
  everyone: ALL,
  assetReaders: ASSET_READERS,
  recordReaders: RECORD_READERS,
  financeReaders: ["group_finance", "sbu_finance", "auditor"],
  gateLogReaders: ["group_it", "sbu_it", "gate_officer", "auditor"],
  gatePassReaders: ["group_finance", "group_it", "sbu_it", "sbu_finance", "gate_officer", "hod", "sbu_admin", "auditor"],
  gatePassRaisers: ["group_it", "sbu_it", "sbu_admin"],
  gatePassApprovers: ["group_it", "sbu_it", "sbu_finance", "hod", "sbu_admin"],
  movers: ["group_it", "sbu_it", "sbu_admin", "gate_officer"],
  lifecycleWriters: ["group_it", "sbu_it", "stores", "sbu_admin"],
  technicalEditors: ["group_it", "sbu_it", "sbu_admin"],
  registrars: ["group_it", "sbu_it", "stores", "sbu_admin"],
  insightManagers: ["group_it", "sbu_it", "sbu_admin"],
  locationManagers: ["group_it", "sbu_admin"],
  sbuManagers: ["group_it", "system_admin"],
  readerViewers: ["group_it", "sbu_it", "gate_officer", "auditor"],
  rfidSimulator: ["group_it"],
  bcViewers: ["group_finance", "group_it", "system_admin", "auditor"],
  bcOperators: ["group_finance", "system_admin"],
  userManagers: ["group_it", "sbu_it"],
  peopleReaders: ["group_finance", "group_it", "sbu_it", "sbu_finance", "hod", "sbu_hr", "stores", "sbu_admin", "auditor"],
  peopleManagers: ["sbu_hr"],
  administrators: ["group_finance", "group_it", "sbu_admin", "system_admin"],
  scanners: ALL.filter((r) => r !== "system_admin"),
  // Phase 4c (Values.cs GateOfficers, VerificationReaders).
  gateOfficers: ["gate_officer", "group_it", "sbu_it", "sbu_admin"],
  // Phase 4d (Values.cs FleetReaders, ItReaders, FinanceWriters).
  fleetReaders: ["sbu_admin", "sbu_finance", "group_finance", "group_it", "auditor"],
  itReaders: ["group_it", "sbu_it", "auditor"],
  importers: ["sbu_finance", "group_finance", "sbu_admin"],
  verificationReaders: ["group_it", "sbu_it", "stores", "sbu_admin", "sbu_finance", "group_finance", "auditor"],
} satisfies Record<string, UserRole[]>;

export type RoleGroup = keyof typeof ROLE_GROUPS;

/** Whether `user` holds one of the roles of `group`. Auditors are read-only whatever the group says. */
export function inGroup(user: User | null, group: RoleGroup): boolean {
  // The System Administrator may do everything (product owner, 29 Oct 2026); the API logs each of their requests.
  return !!user && (user.role === "system_admin" || (ROLE_GROUPS[group] as UserRole[]).includes(user.role));
}

/** The roles of a group, for "you need one of …" messages. */
export const groupNames = (group: RoleGroup) => (ROLE_GROUPS[group] as UserRole[]).map((r) => ROLE_NAME[r]).join(", ");

export const roleName = (role: string) => ROLE_NAME[role as UserRole] ?? role;

/** Roles whose scope is every SBU (the SBU filter is shown to them). */
export const isGroupWide = (user: User | null) => !!user && (["group_finance", "group_it", "auditor", "system_admin"] as UserRole[]).includes(user.role);
