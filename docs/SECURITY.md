# Repository safety rules

This repository must contain source code and public static assets only.

Before every push:

1. Run `scripts/verify-public-snapshot.sh`.
2. Confirm no runtime data, reports, logs, backups or database files are staged.
3. Confirm no credentials, authentication material, user login, account number
   or production configuration is staged.
4. Review `git diff --cached` manually.

If sensitive material is detected, remove it from the snapshot before commit.
Do not rely on `.gitignore` after a file has already been staged.
