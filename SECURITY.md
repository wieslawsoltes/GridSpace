# Security

GridSpace is an early alpha. It is not a validated environment for processing hostile workbooks or for making safety-critical decisions. The current implementation bounds file sizes, ZIP/XML expansion, stored cells and many rectangular operations, but these safeguards are not a security certification.

Do not publish confidential workbooks, credentials or personal information in public issues. Report vulnerabilities through GitHub's private vulnerability reporting facility when available; otherwise contact the maintainer privately before sharing exploit details.

A useful report includes the affected commit, platform/browser, minimal sanitized reproduction, expected behavior and the impact. Do not execute a suspected malicious macro, follow untrusted workbook links or disclose third-party data to demonstrate an issue.

The default formula evaluator does not provide network access, script execution or an assembly-loading feature. Extension functions are trusted host code, not workbook-supplied code. Browser recovery is local IndexedDB storage, not encrypted cloud backup. Export native files before clearing browser storage, and keep originals when importing the supported XLSX subset.
