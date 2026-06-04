import type { MailboxPairDto } from "../api/types";

// Note: simple comma split — RFC 4180 quoting/escaping is NOT supported.
// Mailbox addresses must not contain commas or wrapping quotes.
export function parsePairsCsv(text: string): { pairs: MailboxPairDto[]; errors: string[] } {
  const pairs: MailboxPairDto[] = [];
  const errors: string[] = [];
  const lines = text.split(/\r?\n/);
  let dataRowIndex = 0;
  lines.forEach((raw, lineIndex) => {
    const line = raw.trim();
    if (!line) return; // skip blank lines (lineIndex still tracks the real file line)
    if (dataRowIndex === 0 && /source_mailbox/i.test(line)) { dataRowIndex++; return; } // header
    const cols = line.split(",").map((c) => c.trim());
    if (cols.length !== 2 || !cols[0] || !cols[1]) {
      errors.push(`Line ${lineIndex + 1}: expected exactly 2 columns "source,destination"`);
      dataRowIndex++;
      return;
    }
    pairs.push({ sourceMailbox: cols[0], destMailbox: cols[1] });
    dataRowIndex++;
  });
  return { pairs, errors };
}
