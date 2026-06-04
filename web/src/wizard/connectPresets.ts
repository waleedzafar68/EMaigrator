export const WORKMAIL_REGIONS = ["us-east-1", "us-west-2", "eu-west-1"] as const;
export type WorkmailRegion = (typeof WORKMAIL_REGIONS)[number];

export function workmailHost(region: WorkmailRegion): string {
  return `imap.mail.${region}.awsapps.com`;
}

export const imapDefaults = { port: 993, ssl: true } as const;
