# Release infrastructure

Phase 1 changes workflow/distribution only. Plugin identity, gameplay, configuration, positions, themes and input behavior are unchanged.

Build/test and validate the public release ZIP before invoking the shared distribution action at `4bbc5ce50837b37c8cfaee5c39a075c4f68858ed`. It promotes only this child's registered entry in root `repo.json` with the normal repository-scoped GITHUB_TOKEN, preserves other entries and retries conditional-write conflicts. No child writes the central Sentinel manifest. Published assets are immutable; same-source revalidation never replaces an asset. Follow the owning workflow's version/trigger rules.

DALAMUD_CATALOG_TOKEN is optional and used only as the shared action's notification-token. It may be deleted from this repository if slower hourly reconciliation is acceptable; do not revoke a token still used elsewhere. Missing/rejected notification does not skip verification. The action waits up to 90 minutes for the exact public child and central entry; delayed/dropped scheduled runs can still cause a visible timeout. Repository GITHUB_TOKEN contents:write remains required for publishing releases and changed child manifests.

Release Infrastructure Checks runs actionlint, parses PowerShell files, rejects asset clobber/token misuse, and verifies the currently published entries without write or notification credentials. Existing build/package tests still run. These checks do not claim a new release was published or replace in-game acceptance.

For an interrupted catalog update, use central Update Sentinel Catalog then run Release Infrastructure Checks to verify existing public entries. If publication stopped before child promotion, rerun the original same-source release where supported; otherwise prepare a reviewed child-manifest recovery after verifying the existing public ZIP. Never overwrite an old asset to repair distribution.
