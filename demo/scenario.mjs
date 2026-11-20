// Scripted walkthrough for the README demo. Run: cd demo && npm run record
// The API runs with the in-memory event store, so every recording starts from the same seeded events.
export default async ({ page, step, shot }) => {
  const pendingRow = () => page.getByRole("row", { name: /DD-1001-02 · requested/ });

  await step("Chronos: a loan's balance is replayed from its immutable event stream", async () => {
    await page.getByText("Tidewater Court Developments").first().waitFor();
    await page.getByText("ties to the ledger").waitFor();
  });
  await shot("ledger");
  await step("A £350,000 drawdown awaits sign-off: shown apart, and not counted in the balance", async () => {
    await pendingRow().hover();
  });
  await step("Four-eyes rule: ops-17 requested it, so ops-17 cannot approve it", async () => {
    await pendingRow().getByRole("button", { name: "Approve" }).click();
    await page.getByRole("alert").waitFor();
  });
  await shot("four-eyes");
  await step("Finance approver fin-04 signs off: the drawdown joins the balance and the statement still ties", async () => {
    await page.getByLabel("Acting as").selectOption("fin-04");
    await pendingRow().getByRole("button", { name: "Approve" }).click();
    await page.getByText("Nothing awaiting sign-off.").waitFor();
  });
  await step("As of 1 February 2026: the same loan replayed to a past date, before repayments and the new drawdown", async () => {
    await page.getByLabel("Balance as of date").fill("2026-02-01");
    await page.getByLabel("Balance as of date").evaluate(e => e.blur());
    await page.getByText("ties to the ledger").waitFor();
    await page.waitForTimeout(800);
  });
  await shot("as-of");
};

export async function mobile({ page, shot }) {
  await page.getByText("ties to the ledger").waitFor();
  await shot("mobile");
}
