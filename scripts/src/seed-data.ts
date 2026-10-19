/**
 * Demo data for `pnpm seed`. SBU names, people and addresses are illustrative
 * placeholders for the TSL Group demo — edit freely.
 *
 * Every timestamp is expressed as "days ago" (plus a local time of day) so the
 * demo always looks fresh, whenever the seed is run.
 */

export interface SbuSeed {
  code: string;
  name: string;
  description: string;
  headOffice: string;
  color: string;
}

export const SBUS: SbuSeed[] = [
  { code: "TSAS", name: "Tobacco Sales Administration Services", description: "Auction administration, grower registration and group IT", headOffice: "TSAS House, Harare", color: "#2563EB" },
  { code: "TSF", name: "Tobacco Sales Floor", description: "Tobacco auction floor operations and grading", headOffice: "Auction Floor Complex, Harare", color: "#D97706" },
  { code: "BAK", name: "Bak Logistics", description: "Warehousing, freight and distribution", headOffice: "BAK Depot, Harare", color: "#DC2626" },
  { code: "AGRI", name: "Agricura", description: "Agro-chemicals and farming inputs", headOffice: "Agricura House, Harare", color: "#16A34A" },
  { code: "PROP", name: "TSL Property Services", description: "Group head office, real estate and facilities", headOffice: "TSL Group Head Office, Harare", color: "#7C3AED" },
];

export type LocationType = "office" | "gate" | "warehouse" | "meeting_room" | "lab" | "reception" | "server_room";

export interface LocationSeed {
  key: string;
  sbu: string;
  name: string;
  type: LocationType;
  building: string;
  floor?: string;
  description?: string;
  /** Name of the RFID reader installed here (if any). */
  reader?: string;
}

export const LOCATIONS: LocationSeed[] = [
  { key: "tsas-it", sbu: "TSAS", name: "TSAS House – IT Department", type: "office", building: "TSAS House", floor: "2", reader: "TSAS IT Dept Reader" },
  { key: "tsas-finance", sbu: "TSAS", name: "TSAS House – Finance Office", type: "office", building: "TSAS House", floor: "1" },
  { key: "tsas-board", sbu: "TSAS", name: "TSAS House – Boardroom", type: "meeting_room", building: "TSAS House", floor: "3" },
  { key: "tsas-server", sbu: "TSAS", name: "TSAS House – Server Room", type: "server_room", building: "TSAS House", floor: "G", reader: "TSAS Server Room Reader", description: "Restricted access — group core network and backups" },
  { key: "tsas-gate", sbu: "TSAS", name: "TSAS Main Gate", type: "gate", building: "TSAS House", reader: "TSAS Main Gate Reader", description: "Vehicle and pedestrian exit" },
  { key: "tsf-office", sbu: "TSF", name: "TSF Auction Floor Office", type: "office", building: "Auction Floor Complex" },
  { key: "tsf-lab", sbu: "TSF", name: "TSF Grading Lab", type: "lab", building: "Auction Floor Complex", reader: "TSF Grading Lab Reader" },
  { key: "tsf-gate", sbu: "TSF", name: "TSF Main Gate", type: "gate", building: "Auction Floor Complex", reader: "TSF Main Gate Reader" },
  { key: "bak-office", sbu: "BAK", name: "BAK Depot – Operations Office", type: "office", building: "BAK Depot" },
  { key: "bak-warehouse", sbu: "BAK", name: "BAK Warehouse A", type: "warehouse", building: "BAK Depot", reader: "BAK Warehouse A Reader" },
  { key: "bak-gate", sbu: "BAK", name: "BAK Depot Gate", type: "gate", building: "BAK Depot", reader: "BAK Depot Gate Reader" },
  { key: "agri-office", sbu: "AGRI", name: "Agricura – Sales Office", type: "office", building: "Agricura House", reader: "Agricura Sales Office Reader" },
  { key: "agri-lab", sbu: "AGRI", name: "Agricura – Agro Lab", type: "lab", building: "Agricura House" },
  { key: "agri-gate", sbu: "AGRI", name: "Agricura Gate", type: "gate", building: "Agricura House", reader: "Agricura Gate Reader" },
  { key: "prop-reception", sbu: "PROP", name: "TSL Group Reception", type: "reception", building: "TSL Group Head Office", floor: "G", reader: "Group Reception Reader" },
  { key: "prop-conference", sbu: "PROP", name: "TSL Head Office – Conference Centre", type: "meeting_room", building: "TSL Group Head Office", floor: "1" },
  { key: "prop-gate", sbu: "PROP", name: "TSL Head Office Gate", type: "gate", building: "TSL Group Head Office", reader: "Head Office Gate Reader" },
];

/** An extra reader that is offline, to show the simulator's inactive state. */
export const INACTIVE_READER = { name: "BAK Dock Door Reader (offline)", location: "bak-warehouse" };

export interface AssetSeed {
  key: string;
  sbu: string;
  name: string;
  category: string;
  status: "active" | "maintenance" | "retired" | "missing";
  assignedTo?: string;
  /** Where the asset sits once all seeded movements have happened (null = no location). */
  location: string | null;
  /** Other locations (same SBU) the asset moved between historically. */
  visits?: string[];
  /** Days since the last movement (drives "not moved in 30 days"). */
  lastMovedDaysAgo: number;
  /** Number of historical movements to generate. */
  moves: number;
  registeredDaysAgo: number;
  value: number;
  notes?: string;
}

export const ASSETS: AssetSeed[] = [
  // ── TSAS ──
  { key: "dell-5440", sbu: "TSAS", name: "Dell Latitude 5440", category: "Laptop", status: "active", assignedTo: "John Doe", location: "tsas-it", visits: ["tsas-board", "tsas-finance"], lastMovedDaysAgo: 1, moves: 6, registeredDaysAgo: 210, value: 1350 },
  { key: "hp-840", sbu: "TSAS", name: "HP EliteBook 840 G10", category: "Laptop", status: "active", assignedTo: "Tendai Moyo", location: "tsas-finance", visits: ["tsas-board"], lastMovedDaysAgo: 8, moves: 5, registeredDaysAgo: 320, value: 1480 },
  { key: "lenovo-t14", sbu: "TSAS", name: "Lenovo ThinkPad T14", category: "Laptop", status: "active", assignedTo: "Rudo Chikwanha", location: "tsas-it", visits: ["tsas-server", "tsas-board"], lastMovedDaysAgo: 0, moves: 7, registeredDaysAgo: 400, value: 1290 },
  { key: "optiplex", sbu: "TSAS", name: "Dell OptiPlex 7010", category: "Desktop", status: "active", assignedTo: "Farai Ncube", location: "tsas-finance", lastMovedDaysAgo: 5, moves: 1, registeredDaysAgo: 500, value: 890 },
  { key: "epson-x51", sbu: "TSAS", name: "Epson EB-X51 Projector", category: "Projector", status: "active", location: "tsas-board", visits: ["tsas-it"], lastMovedDaysAgo: 45, moves: 3, registeredDaysAgo: 600, value: 640 },
  { key: "cisco-9200", sbu: "TSAS", name: "Cisco Catalyst 9200 Switch", category: "Network Equipment", status: "active", location: "tsas-server", visits: ["tsas-it"], lastMovedDaysAgo: 60, moves: 1, registeredDaysAgo: 700, value: 2450 },
  { key: "apc-ups", sbu: "TSAS", name: "APC Smart-UPS 3000", category: "UPS", status: "active", location: "tsas-server", visits: ["tsas-it"], lastMovedDaysAgo: 50, moves: 1, registeredDaysAgo: 650, value: 1800 },
  { key: "galaxy-s23", sbu: "TSAS", name: "Samsung Galaxy S23", category: "Phone", status: "active", assignedTo: "Nyasha Dube", location: "tsas-it", visits: ["tsas-finance"], lastMovedDaysAgo: 2, moves: 4, registeredDaysAgo: 180, value: 820 },
  { key: "laserjet", sbu: "TSAS", name: "HP LaserJet Pro M404", category: "Printer", status: "maintenance", location: "tsas-finance", lastMovedDaysAgo: 18, moves: 1, registeredDaysAgo: 900, value: 380, notes: "Paper feed fault — with vendor" },
  // ── TSF ──
  { key: "dell-7440", sbu: "TSF", name: "Dell Latitude 7440", category: "Laptop", status: "active", assignedTo: "Tatenda Mhlanga", location: "tsf-office", visits: ["tsf-lab"], lastMovedDaysAgo: 8, moves: 4, registeredDaysAgo: 150, value: 1520 },
  { key: "thinkcentre", sbu: "TSF", name: "Lenovo ThinkCentre M70", category: "Desktop", status: "active", assignedTo: "Kudzai Banda", location: "tsf-lab", lastMovedDaysAgo: 4, moves: 1, registeredDaysAgo: 420, value: 760 },
  { key: "zebra-zt411", sbu: "TSF", name: "Zebra ZT411 Label Printer", category: "Printer", status: "active", location: "tsf-office", visits: ["tsf-lab"], lastMovedDaysAgo: 40, moves: 2, registeredDaysAgo: 380, value: 1950 },
  { key: "moisture", sbu: "TSF", name: "Moisture Analyser MA-100", category: "Other", status: "active", location: "tsf-lab", visits: ["tsf-office"], lastMovedDaysAgo: 6, moves: 3, registeredDaysAgo: 520, value: 3100 },
  { key: "ipad", sbu: "TSF", name: "iPad Air (5th gen)", category: "Other", status: "active", assignedTo: "Tatenda Mhlanga", location: "tsf-office", visits: ["tsf-lab"], lastMovedDaysAgo: 3, moves: 3, registeredDaysAgo: 260, value: 700 },
  { key: "benq", sbu: "TSF", name: "BenQ MW560 Projector", category: "Projector", status: "missing", location: "tsf-office", lastMovedDaysAgo: 25, moves: 2, registeredDaysAgo: 610, value: 560, notes: "Not seen since the September grower meeting" },
  // ── BAK ──
  { key: "hilux", sbu: "BAK", name: "Toyota Hilux 2.4 GD-6", category: "Vehicle", status: "active", assignedTo: "Simbarashe Chikore", location: "bak-office", visits: ["bak-warehouse"], lastMovedDaysAgo: 2, moves: 5, registeredDaysAgo: 450, value: 41500 },
  { key: "dell-3540", sbu: "BAK", name: "Dell Latitude 3540", category: "Laptop", status: "active", assignedTo: "Blessing Mutasa", location: "bak-office", visits: ["bak-warehouse"], lastMovedDaysAgo: 14, moves: 4, registeredDaysAgo: 230, value: 980 },
  { key: "tc52", sbu: "BAK", name: "Zebra TC52 Handheld Scanner", category: "Other", status: "active", assignedTo: "Blessing Mutasa", location: "bak-warehouse", visits: ["bak-office"], lastMovedDaysAgo: 8, moves: 4, registeredDaysAgo: 300, value: 1150 },
  { key: "forklift", sbu: "BAK", name: "Toyota 8FBE Forklift", category: "Vehicle", status: "active", location: "bak-warehouse", visits: ["bak-office"], lastMovedDaysAgo: 35, moves: 2, registeredDaysAgo: 800, value: 23800 },
  { key: "probook", sbu: "BAK", name: "HP ProBook 450 G9", category: "Laptop", status: "active", location: "bak-warehouse", visits: ["bak-office"], lastMovedDaysAgo: 6, moves: 2, registeredDaysAgo: 340, value: 870 },
  { key: "unifi", sbu: "BAK", name: "Ubiquiti UniFi U6 Access Point", category: "Network Equipment", status: "active", location: "bak-warehouse", lastMovedDaysAgo: 5, moves: 1, registeredDaysAgo: 290, value: 210 },
  // ── AGRI ──
  { key: "hp-650", sbu: "AGRI", name: "HP EliteBook 650 G10", category: "Laptop", status: "active", assignedTo: "Chipo Sibanda", location: "agri-office", visits: ["agri-lab"], lastMovedDaysAgo: 2, moves: 4, registeredDaysAgo: 190, value: 1210 },
  { key: "precision", sbu: "AGRI", name: "Dell Precision 3660", category: "Desktop", status: "active", assignedTo: "Ruvimbo Zhou", location: "agri-lab", lastMovedDaysAgo: 3, moves: 1, registeredDaysAgo: 360, value: 2300 },
  { key: "balance", sbu: "AGRI", name: "Mettler Toledo XS205 Balance", category: "Other", status: "active", location: "agri-lab", visits: ["agri-office"], lastMovedDaysAgo: 55, moves: 2, registeredDaysAgo: 720, value: 5400 },
  { key: "galaxy-a54", sbu: "AGRI", name: "Samsung Galaxy A54", category: "Phone", status: "active", assignedTo: "Chipo Sibanda", location: "agri-office", visits: ["agri-lab"], lastMovedDaysAgo: 4, moves: 3, registeredDaysAgo: 160, value: 430 },
  { key: "epson-w49", sbu: "AGRI", name: "Epson EB-W49 Projector", category: "Projector", status: "active", location: "agri-office", lastMovedDaysAgo: 6, moves: 1, registeredDaysAgo: 410, value: 720 },
  // ── PROP ──
  { key: "macbook", sbu: "PROP", name: "Apple MacBook Pro 14", category: "Laptop", status: "active", assignedTo: "Tsitsi Marufu", location: "prop-reception", visits: ["prop-conference"], lastMovedDaysAgo: 1, moves: 5, registeredDaysAgo: 140, value: 2350 },
  { key: "polycom", sbu: "PROP", name: "Poly Studio X50 Video Bar", category: "Other", status: "active", location: "prop-conference", lastMovedDaysAgo: 5, moves: 1, registeredDaysAgo: 380, value: 2900 },
  { key: "canon", sbu: "PROP", name: "Canon imageRUNNER C3226", category: "Printer", status: "active", location: "prop-reception", lastMovedDaysAgo: 4, moves: 1, registeredDaysAgo: 540, value: 4200 },
  { key: "aeron", sbu: "PROP", name: "Herman Miller Aeron Chair", category: "Furniture", status: "active", location: "prop-conference", lastMovedDaysAgo: 5, moves: 2, registeredDaysAgo: 1000, value: 1400 },
  { key: "x1-carbon", sbu: "PROP", name: "Lenovo ThinkPad X1 Carbon", category: "Laptop", status: "active", assignedTo: "Tapiwa Chirwa", location: "prop-conference", visits: ["prop-reception"], lastMovedDaysAgo: 2, moves: 4, registeredDaysAgo: 120, value: 2100 },
  { key: "epson-x41", sbu: "PROP", name: "Epson EB-X41 Projector", category: "Projector", status: "retired", location: null, lastMovedDaysAgo: 95, moves: 1, registeredDaysAgo: 1400, value: 450, notes: "Disposed — lamp housing cracked" },
];

/** People who approve gate passes in the demo. */
export const APPROVERS = { tsas: "Rudo Chikwanha", bak: "Simbarashe Chikore", prop: "Tsitsi Marufu" };
