import { assetsTable, locationsTable, rfidReadersTable, sbusTable } from "@workspace/db/schema";
import { testDb } from "./test-db";

/**
 * A small TSL world: two SBUs, an office and a gate per SBU, readers at each,
 * and two TSAS assets sitting in the TSAS IT office.
 */
export async function seedBasics() {
  const [tsas, bak] = await testDb
    .insert(sbusTable)
    .values([
      { code: "TSAS", name: "Tobacco Sales Administration Services", color: "#2563EB" },
      { code: "BAK", name: "Bak Logistics", color: "#DC2626" },
    ])
    .returning();

  const [office, gate, bakGate, bakWarehouse] = await testDb
    .insert(locationsTable)
    .values([
      { name: "TSAS IT Office", type: "office", building: "TSAS House", sbuId: tsas.id },
      { name: "TSAS Main Gate", type: "gate", building: "TSAS House", sbuId: tsas.id },
      { name: "BAK Depot Gate", type: "gate", building: "BAK Depot", sbuId: bak.id },
      { name: "BAK Warehouse", type: "warehouse", building: "BAK Depot", sbuId: bak.id },
    ])
    .returning();

  const [officeReader, gateReader, bakGateReader, inactiveReader] = await testDb
    .insert(rfidReadersTable)
    .values([
      { name: "TSAS Office Reader", locationId: office.id },
      { name: "TSAS Gate Reader", locationId: gate.id },
      { name: "BAK Gate Reader", locationId: bakGate.id },
      { name: "TSAS Spare Reader", locationId: office.id, status: "inactive" },
    ])
    .returning();

  const [laptop, projector] = await testDb
    .insert(assetsTable)
    .values([
      {
        name: "Dell Latitude 5440",
        category: "Laptop",
        serialNumber: "SN-DL-001",
        rfidTag: "TAG-DL-001",
        sbuId: tsas.id,
        currentLocationId: office.id,
        assignedTo: "John Doe",
      },
      {
        name: "Epson EB-X51 Projector",
        category: "Projector",
        serialNumber: "SN-EP-001",
        rfidTag: "TAG-EP-001",
        sbuId: tsas.id,
        currentLocationId: office.id,
      },
    ])
    .returning();

  return {
    sbus: { tsas, bak },
    locations: { office, gate, bakGate, bakWarehouse },
    readers: { officeReader, gateReader, bakGateReader, inactiveReader },
    assets: { laptop, projector },
  };
}

export type World = Awaited<ReturnType<typeof seedBasics>>;
