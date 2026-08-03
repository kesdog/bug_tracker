---
name: chained-bug-report-with-images
description: "Chain bug-reporting-excellence output with inline image decoration compatible with [[img:n]] tokens."
category: quality-communication
priority: high
tokenEstimate: 700
dependencies: [bug-reporting-excellence]
tags: [bugs, reporting, images, tokens, integration]
validation:
  schema_path: schemas/output.json
---

# Chained Bug Report With Images

Use this skill when you want a clean report text produced by `bug-reporting-excellence` and then decorated with inline image tokens that the Bug Tracker report builder can parse.

## Pipeline

1. Generate report text with `bug-reporting-excellence` sections.
2. Keep image evidence list as ordered `reportImages[]`.
3. Convert image references to `[[img:n]]` tokens.
4. Ensure every uploaded image is referenced; append `Evidence [[img:n]]` for any unreferenced image.

## Accepted image tag inputs

- `[[img:0]]` (already indexed)
- `[[img:filename.png]]`
- `[[img:"filename with spaces.png"]]`

Output must always use indexed tokens only (`[[img:n]]`).

## Output contract

Return JSON matching `schemas/output.json`.

### Rules

- `reportText` must include the quality sections from bug-reporting-excellence.
- `reportText` can include only indexed image tokens in final output.
- `reportImages` max 5 items.
- Every token index must exist in `reportImages`.
- Any unresolved name references must be listed in `unresolvedImageRefs`.

## Example

Input text from authoring stage:

```text
### Actual Behavior
Checkout times out after 30s [[img:"checkout-timeout.png"]]
```

Final output text:

```text
### Actual Behavior
Checkout times out after 30s [[img:0]]
```
