# Agent Skills Export

This folder contains skills that can be copied into OpenCode skill paths for local or shared use.

## Included

- `chained-bug-report-with-images/`
  - Chains report quality guidance with image token decoration (`[[img:n]]`).

## Install locally (project)

Copy the skill folder into:

- `.opencode/skills/.agents/skills/`

Resulting path example:

- `.opencode/skills/.agents/skills/chained-bug-report-with-images/SKILL.md`

## Install globally (user)

Copy to one of your configured `skills.paths` directories.

## Notes

- Final report text should use indexed image tokens only.
- Keep `reportImages` ordering stable so token indexes remain valid.
