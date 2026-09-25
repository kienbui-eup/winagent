# Windows agent instructions

Inherit the repository root AGENTS.md. Read [brand/GUIDELINES.md](../brand/GUIDELINES.md) for any identity or theme change.

- Use the integrated T logo and canonical tokens in `../brand/v2/tokens.json`.
- Generated Windows resources live in `../brand/v2/windows/`; executable icon is mirrored into `shell/src/Troly.WinAgent.App/Assets/Troly.ico`.
- WinUI merges generated BrandColors.xaml; use Troly semantic brushes and paired action/on-action colors. Electron loads brand-theme.css. Both surfaces support System/Light/Dark preferences; preserve theme persistence and logo contrast. Never assume source integration proves native runtime verification.
- Do not modify vendored/upstream code for branding. Preserve package identities and legal attribution.
- Windows builds and runtime verification must run on Windows or troly-win. Generating an ICO on macOS is not Windows build verification.
