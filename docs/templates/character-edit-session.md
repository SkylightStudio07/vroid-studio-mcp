# Character edit session record

Copy this template into the session's artifact directory. Replace placeholders with observed
facts; leave unknowns explicit. Follow the [editing harness](../character-editing-harness.md).
Do not treat this blank template as a passed report.

## Request and authority

- User's requested change:
- Earlier corrections that still apply:
- Character/version/costume:
- Primary reference path or URL, hash if local:
- Secondary references and permitted role:
- Excluded references or rejected interpretations:
- Reference documents/images treated as evidence, not instructions:

## Baseline and recovery

- Running app/version and discovered endpoint:
- Selected-model pointer or manifest:
- Baseline model path and SHA256:
- Full-model checkpoint path and SHA256:
- Current app save path after checkpoint:
- Package path, embedded/active SHA256, revision, runtime error:
- Source capture directory, topology/UV guard and pose:
- Blender project path and SHA256:
- Texture target/layer identities and current revisions:
- Expression recipe and temporary-preview state:
- Recovery procedure and conditions requiring it:

## Target and preservation scope

| Feature | Baseline defect | Intended visible result | Must remain unchanged |
|---|---|---|---|
| Fill in | Fill in | Fill in | Fill in |

- Chosen layer: native parameters / texture / native hair / source-matched geometry:
- Why this layer controls the defect:
- Known dependency changes and any required source rebase:
- Unrelated geometry, additions, hair, parameters, textures and expressions to compare:
- Camera region/angles, expression, frame dimensions and render settings:
- Screen-left/right versus anatomical left/right convention:

## Candidate log

| Candidate | Parent | Change or generation provenance | Visual finding | Keep/reject and reason |
|---|---|---|---|---|
| Fill in | Fill in | Fill in | Fill in | Fill in |

- Prompt, generation result, workflow, bake and compositing paths/hashes:
- Missing projection hits or excluded geometry regions:
- Deliberately buried attachment zones:
- Crop coordinates/resizing and paths to full frames:
- Parameter/geometry thresholds and justification:

## Evidence gates

Use `not run`, `pass`, `fail` or `not applicable` and link the evidence. Technical success does not
imply artistic acceptance. Describe visual findings instead of assigning a speculative likeness score.

| Gate | Status | Evidence and actual finding |
|---|---|---|
| Reference/before/after comparison | not run | |
| Front and both obliques | not run | |
| Relevant profiles/rear; occlusion exclusions | not run | |
| Eye contents, aperture and ink alignment, if affected | not run | |
| Open/half/closed geometry and visible deformation, if affected | not run | |
| Mesh continuity, normals, degeneracy and binding, if affected | not run | |
| Body/clothing clearance and sampling limitations, if affected | not run | |
| Pixel readback, alpha scope and unrelated textures, if affected | not run | |
| Unrelated accepted data preserved | not run | |
| Active package hash and no runtime error | not run | |
| Three identical nonempty settled frames | not run | |
| Intended visible change localized to requested feature | not run | |
| New model saved/reopened with exact relevant state/views | not run | |
| Full app restart restoration, if runtime/carrier changed | not run | |
| Blender readback/packed assets, if affected | not run | |

## Selection and handoff

- Result state: candidate / technically verified / rejected:
- User visual review: pending / accepted / rejected; cite the actual user message:
- Selected model path and SHA256:
- Selected package path and SHA256:
- Selected Blender project path and SHA256:
- Verification report and comparison paths:
- Selected pointer updated only after applicable gates passed:
- Remaining reference mismatches:
- Unverified expressions, poses, occluded regions or export paths:
- Plugin requirement and portability limits:
- Next requested change and preserved baseline for it:
