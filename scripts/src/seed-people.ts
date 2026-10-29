/**
 * Phase 2 demo data for `pnpm seed`: departments and employees per SBU, the asset holders linked to their
 * employee records, and one sign-in for each role Phase 2 adds (password = username + "-2026").
 * Names are illustrative.
 */

export const DEPARTMENTS = ["IT", "Finance", "Operations", "Administration"] as const;
export type DepartmentName = (typeof DEPARTMENTS)[number];

export interface EmployeeSeed {
  no: string;
  sbu: string;
  name: string;
  department: DepartmentName;
  jobRole: string;
  /** Employee number of the line manager. */
  manager?: string;
}

/** Everyone who holds a demo asset, plus the heads of department the HOD sign-in needs. */
export const EMPLOYEES: EmployeeSeed[] = [
  { no: "TSAS-0001", sbu: "TSAS", name: "Rudo Chikwanha", department: "Operations", jobRole: "Head of Operations" },
  { no: "TSAS-0002", sbu: "TSAS", name: "John Doe", department: "Operations", jobRole: "Grower Liaison Officer", manager: "TSAS-0001" },
  { no: "TSAS-0003", sbu: "TSAS", name: "Tendai Moyo", department: "Operations", jobRole: "Registration Clerk", manager: "TSAS-0001" },
  { no: "TSAS-0004", sbu: "TSAS", name: "Farai Ncube", department: "IT", jobRole: "IT Technician" },
  { no: "TSAS-0005", sbu: "TSAS", name: "Nyasha Dube", department: "Finance", jobRole: "Accountant" },
  { no: "TSF-0001", sbu: "TSF", name: "Tatenda Mhlanga", department: "Operations", jobRole: "Floor Manager" },
  { no: "TSF-0002", sbu: "TSF", name: "Kudzai Banda", department: "Operations", jobRole: "Grader", manager: "TSF-0001" },
  { no: "BAK-0001", sbu: "BAK", name: "Simbarashe Chikore", department: "Administration", jobRole: "Admin Manager" },
  { no: "BAK-0002", sbu: "BAK", name: "Blessing Mutasa", department: "IT", jobRole: "IT Administrator" },
  { no: "AGRI-0001", sbu: "AGRI", name: "Chipo Sibanda", department: "Administration", jobRole: "Admin Manager" },
  { no: "AGRI-0002", sbu: "AGRI", name: "Ruvimbo Zhou", department: "Operations", jobRole: "Agronomist", manager: "AGRI-0001" },
  { no: "PROP-0001", sbu: "PROP", name: "Tsitsi Marufu", department: "Finance", jobRole: "Group Accountant" },
  { no: "PROP-0002", sbu: "PROP", name: "Tapiwa Chirwa", department: "IT", jobRole: "Systems Analyst" },
  { no: "PROPH-0001", sbu: "PROP-H", name: "Kudakwashe Moyo", department: "Operations", jobRole: "Plant Supervisor" },
];

/** The roles Phase 2 adds, with the System Administrator and an employee who holds items. */
export const PHASE2_USERS = [
  { username: "sys.admin", name: "Tafara Mapfumo", role: "system_admin", sbu: null, employee: null },
  { username: "tsas.gate", name: "Gift Mahachi", role: "gate_officer", sbu: "TSAS", employee: null },
  { username: "tsas.hod", name: "Rudo Chikwanha", role: "hod", sbu: "TSAS", employee: "TSAS-0001" },
  { username: "tsas.hr", name: "Memory Gwatidzo", role: "sbu_hr", sbu: "TSAS", employee: null },
  { username: "tsas.stores", name: "Tinashe Mukanya", role: "stores", sbu: "TSAS", employee: null },
  { username: "john.doe", name: "John Doe", role: "employee", sbu: "TSAS", employee: "TSAS-0002" },
] as const;

/** A one-page PDF standing in for a scanned supplier invoice (attached to the first TSAS laptop). */
export function sampleInvoicePdf(invoiceNo: string, supplier: string, amount: string): Buffer {
  const text = `Tax invoice ${invoiceNo} - ${supplier} - USD ${amount}`.replace(/[()\\]/g, "");
  const stream = `BT /F1 14 Tf 72 720 Td (${text}) Tj ET`;
  const objects = [
    "<< /Type /Catalog /Pages 2 0 R >>",
    "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
    `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`,
    "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
  ];
  let pdf = "%PDF-1.4\n";
  const offsets: number[] = [];
  objects.forEach((body, i) => {
    offsets.push(pdf.length);
    pdf += `${i + 1} 0 obj\n${body}\nendobj\n`;
  });
  const xref = pdf.length;
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n${offsets.map((o) => `${String(o).padStart(10, "0")} 00000 n \n`).join("")}`;
  pdf += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(pdf, "latin1");
}
