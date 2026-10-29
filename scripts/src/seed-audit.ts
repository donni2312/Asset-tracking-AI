/**
 * Phase 1 demo data (AU-01 … AU-12) for `pnpm seed`: each demo asset's category template and values,
 * Business Central (mock) asset numbers, cost and capitalisation, upgrades, and the scoped sign-ins.
 *
 * The values are illustrative. A few gaps are left on purpose so the completeness report (AU-11), the
 * approvals inbox and the "No serial" flow have something to show.
 */
export interface TemplateSpec {
  /** asset_categories.code (migration 0002) */
  code: "COMP" | "MOB" | "NET" | "PER" | "MV" | "PG" | "FF" | "EQ";
  sub: string;
  /** Template values, keyed by field key; the identifier (serial / VIN / IMEI) is generated. */
  attrs: Record<string, string>;
  /** Recorded as "No serial" with this reason (identifier left out). */
  noSerialReason?: string;
  noSerialApproved?: boolean;
  /** Left out of the finance record on purpose (AU-11 demo). */
  missing?: Array<"invoice" | "po" | "cost">;
  /** Not capitalised yet by SBU Finance. */
  notCapitalised?: boolean;
  currency?: { code: string; rate: number };
}

const win11 = { operating_system: "Windows 11 Pro" };

/** One entry per ASSETS key in seed-data.ts. */
export const TEMPLATES: Record<string, TemplateSpec> = {
  "dell-5440": { code: "COMP", sub: "Laptop", attrs: { make: "Dell", model: "Latitude 5440", processor: "i5-1345U", ram: "16", storage: "512 GB SSD", ...win11, mac_address: "A4:5D:36:12:AB:01" } },
  "hp-840": { code: "COMP", sub: "Laptop", attrs: { make: "HP", model: "EliteBook 840 G10", processor: "i7-1355U", ram: "16", storage: "512 GB SSD", ...win11 } },
  "lenovo-t14": { code: "COMP", sub: "Laptop", attrs: { make: "Lenovo", model: "ThinkPad T14", processor: "Ryzen 7 PRO 7840U", ram: "32", storage: "1 TB SSD", ...win11 } },
  optiplex: { code: "COMP", sub: "Desktop", attrs: { make: "Dell", model: "OptiPlex 7010", processor: "i5-13500", ram: "16", storage: "512 GB SSD", ...win11 }, missing: ["invoice"] },
  "epson-x51": { code: "PER", sub: "Projector", attrs: { make: "Epson", model: "EB-X51" } },
  "cisco-9200": { code: "NET", sub: "Network device", attrs: { make: "Cisco", model: "Catalyst 9200", device_type: "Switch", ports_capacity: "48 ports, 4× 10G uplink", ip_address: "10.1.0.2" } },
  "apc-ups": { code: "PER", sub: "UPS", attrs: { make: "APC", model: "Smart-UPS 3000" } },
  "galaxy-s23": { code: "MOB", sub: "Smartphone", attrs: { make: "Samsung", model: "Galaxy S23", storage: "256", phone_number: "+263 77 210 4471" } },
  laserjet: { code: "PER", sub: "Printer", attrs: { make: "HP", model: "LaserJet Pro M404" } },
  "dell-7440": { code: "COMP", sub: "Laptop", attrs: { make: "Dell", model: "Latitude 7440", processor: "i7-1365U", ram: "16", storage: "512 GB SSD", ...win11 } },
  thinkcentre: { code: "COMP", sub: "Desktop", attrs: { make: "Lenovo", model: "ThinkCentre M70", processor: "i5-12400", ram: "8", storage: "256 GB SSD", ...win11 } },
  "zebra-zt411": { code: "PER", sub: "Printer", attrs: { make: "Zebra", model: "ZT411" } },
  moisture: { code: "EQ", sub: "Lab equipment", attrs: { make: "Kett", model: "MA-100" }, missing: ["po"] },
  ipad: { code: "MOB", sub: "Tablet", attrs: { make: "Apple", model: "iPad Air (5th gen)", storage: "64", phone_number: "n/a (Wi-Fi)" } },
  benq: { code: "PER", sub: "Projector", attrs: { make: "BenQ", model: "MW560" } },
  hilux: { code: "MV", sub: "Light vehicle", attrs: { registration_number: "AGR 4471", engine_number: "2GD-0451286", make: "Toyota", model: "Hilux 2.4 GD-6", year: "2021", colour: "White", capacity: "1 t", mileage: "142300", fuel_type: "Diesel" }, currency: { code: "ZAR", rate: 0.054321 } },
  "dell-3540": { code: "COMP", sub: "Laptop", attrs: { make: "Dell", model: "Latitude 3540", processor: "i5-1335U", ram: "8", storage: "256 GB SSD", ...win11 } },
  tc52: { code: "EQ", sub: "Tool", attrs: { make: "Zebra", model: "TC52 Handheld Scanner" } },
  forklift: { code: "PG", sub: "Machinery", attrs: { make: "Toyota", model: "8FBE Forklift", capacity_kva: "48", hours_run: "6120", fuel_type: "Electric" } },
  probook: { code: "COMP", sub: "Laptop", attrs: { make: "HP", model: "ProBook 450 G9", processor: "i5-1235U", ram: "8", storage: "256 GB SSD", ...win11 }, notCapitalised: true },
  unifi: { code: "NET", sub: "Network device", attrs: { make: "Ubiquiti", model: "UniFi U6", device_type: "Access point", ports_capacity: "Wi-Fi 6, 1 GbE" } },
  "hp-650": { code: "COMP", sub: "Laptop", attrs: { make: "HP", model: "EliteBook 650 G10", processor: "i5-1345U", ram: "16", storage: "512 GB SSD", ...win11 } },
  precision: { code: "COMP", sub: "Desktop", attrs: { make: "Dell", model: "Precision 3660", processor: "i9-13900", ram: "64", storage: "2 TB SSD", ...win11 } },
  balance: { code: "EQ", sub: "Lab equipment", attrs: { make: "Mettler Toledo", model: "XS205 Balance" } },
  "galaxy-a54": { code: "MOB", sub: "Smartphone", attrs: { make: "Samsung", model: "Galaxy A54", storage: "128", phone_number: "+263 78 330 1902" } },
  "epson-w49": { code: "PER", sub: "Projector", attrs: { make: "Epson", model: "EB-W49" } },
  macbook: { code: "COMP", sub: "Laptop", attrs: { make: "Apple", model: "MacBook Pro 14", processor: "M3 Pro", ram: "18", storage: "512 GB SSD", operating_system: "macOS 14" } },
  polycom: { code: "PER", sub: "Monitor", attrs: { make: "Poly", model: "Studio X50 Video Bar" } },
  canon: { code: "PER", sub: "Printer", attrs: { make: "Canon", model: "imageRUNNER C3226" } },
  aeron: { code: "FF", sub: "Chair", attrs: { make: "Herman Miller", model: "Aeron", material: "Mesh, aluminium", room: "Conference Centre" }, noSerialReason: "Furniture: no manufacturer serial label", noSerialApproved: true },
  "x1-carbon": { code: "COMP", sub: "Laptop", attrs: { make: "Lenovo", model: "ThinkPad X1 Carbon Gen 11", processor: "i7-1365U", ram: "32", storage: "1 TB SSD", ...win11 } },
  "epson-x41": { code: "PER", sub: "Projector", attrs: { make: "Epson", model: "EB-X41" } },
  // Propak (added for Phase 1)
  "ppk-laptop": { code: "COMP", sub: "Laptop", attrs: { make: "Dell", model: "Latitude 5450", processor: "i5-1345U", ram: "16", storage: "512 GB SSD", ...win11 } },
  "ppk-genset": { code: "PG", sub: "Generator", attrs: { make: "Cummins", model: "C150 D5", capacity_kva: "150", hours_run: "2310", fuel_type: "Diesel" } },
  "ppk-desk": { code: "FF", sub: "Desk", attrs: { make: "Generic", model: "Executive", material: "Oak", room: "Plant office" }, noSerialReason: "Built-in desk, no manufacturer label" },
};

/** Upgrades (AU-07): `daysAgo` logged; capitalised ones from `monthsAgo` months back. */
export const UPGRADES = [
  { key: "dell-5440", description: "RAM upgrade", before: "8 GB", after: "16 GB", cost: "140.00", status: "capitalised", monthsAgo: 4, daysAgo: 125, by: "Farai Ncube", role: "sbu_it", changes: {} },
  { key: "dell-5440", description: "Battery replacement", before: "Worn", after: "New 54 Wh", cost: "85.00", status: "pending", daysAgo: 2, by: "Farai Ncube", role: "sbu_it", changes: {} },
  { key: "cisco-9200", description: "Uplink module", before: "4× 1G uplink", after: "4× 10G uplink", cost: "620.00", status: "capitalised", monthsAgo: 7, daysAgo: 215, by: "Farai Ncube", role: "sbu_it", changes: {} },
  { key: "hilux", description: "Engine overhaul", before: "Worn engine", after: "Rebuilt 2GD", cost: "2800.00", status: "capitalised", monthsAgo: 2, daysAgo: 70, by: "Simbarashe Chikore", role: "sbu_admin", changes: {} },
  { key: "dell-3540", description: "SSD upgrade", before: "256 GB SSD", after: "512 GB SSD", cost: "95.00", status: "pending", daysAgo: 1, by: "Blessing Mutasa", role: "sbu_it", changes: { storage: "512 GB SSD" } },
  { key: "laserjet", description: "Paper feed repair", before: "Paper feed fault", after: "Feed roller replaced", cost: "60.00", status: "expensed", daysAgo: 12, by: "Farai Ncube", role: "sbu_it", changes: {} },
] as const;

/**
 * Scoped sign-ins (Phase 1). Password = username + "-2026" (demo only: change before anyone else can reach
 * the dashboard). The auditor's access expires 60 days after seeding.
 */
export const SCOPED_USERS = [
  { username: "group.finance", name: "Tariro Nyathi", role: "group_finance", sbu: null },
  { username: "group.it", name: "Kudzai Banda", role: "group_it", sbu: null },
  { username: "tsas.it", name: "Farai Ncube", role: "sbu_it", sbu: "TSAS" },
  { username: "tsas.finance", name: "Chipo Mlambo", role: "sbu_finance", sbu: "TSAS" },
  { username: "bak.it", name: "Blessing Mutasa", role: "sbu_it", sbu: "BAK" },
  { username: "bak.finance", name: "Rumbidzai Dube", role: "sbu_finance", sbu: "BAK" },
  { username: "bak.admin", name: "Simbarashe Chikore", role: "sbu_admin", sbu: "BAK" },
  { username: "bak.stores", name: "Tafadzwa Gora", role: "stores", sbu: "BAK" },
  { username: "agri.admin", name: "Chipo Sibanda", role: "sbu_admin", sbu: "AGRI" },
  { username: "ppk.finance", name: "Nokutenda Shumba", role: "sbu_finance", sbu: "PPK" },
  { username: "ext.auditor", name: "External Auditor (demo)", role: "auditor", sbu: null },
] as const;

/** Same pattern rules as the API's TemplateRules.BuildDescription (the API rebuilds it on every change). */
export function describe(pattern: string, sub: string, categoryName: string, fields: { key: string; unit: string | null }[], values: Record<string, string>): string {
  const pascal = (key: string) => key.split("_").map((p) => p.charAt(0).toUpperCase() + p.slice(1)).join("");
  return pattern
    .replace(/\{([A-Za-z0-9]+)\}/g, (_, token: string) => {
      if (token === "SubCategory") return sub;
      if (token === "Category") return categoryName;
      const field = fields.find((f) => pascal(f.key) === token);
      const value = field ? values[field.key] : undefined;
      if (!field || !value) return "";
      return field.unit ? `${value} ${field.unit}` : value;
    })
    .replace(/\s{2,}/g, " ")
    .replace(/\s*,(\s*,)+/g, ",")
    .replace(/\s*–\s*(,\s*)+/g, " – ")
    .replace(/^[\s,–]+|[\s,–]+$/g, "")
    .trim();
}
