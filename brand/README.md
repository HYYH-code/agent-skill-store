# Agent Skill Store brand assets

The Agent Skill Store identity is built around four interlocking modules that form an open repository entrance. The negative space represents a skill moving through discovery, governance, and installation.

## Palette

| Token | Value | Use |
| --- | --- | --- |
| Graphite | `#20262E` | Primary text and structural modules |
| Emerald | `#087F5B` | Primary actions and verified paths |
| Coral | `#E54B5D` | Publishing and focused emphasis |
| Off white | `#F5F7F6` | Main background |
| Border neutral | `#D7DEDA` | Dividers and supporting modules |

## Generation record

- Generated: 2026-07-17
- Generator: OpenAI built-in `image_gen`
- Use cases: `logo-brand` and `illustration-story`
- Logo source: generated on a flat magenta chroma-key background, centered without scaling on a 2048x2048 `#FF00FF` source canvas, extracted with the Codex image-generation chroma-key helper, then reconstructed as deterministic flat SVG paths.
- Login visual: regenerated as a text-free 1536x1024 flat 2D module flow, then deterministically quantized to the five documented palette colors and encoded as lossless WebP.

The source prompts requested an original modular repository symbol and explicitly excluded text, watermarks, shopping bags, robots, brains, lightning bolts, puzzle pieces, anthropomorphic characters, 3D, isometric cubes, gradients, shadows, and resemblance to existing technology company marks. The final login prompt placed abstract module plates, an open repository gate, a validation checkpoint, and destination sockets along the lower half while reserving the upper-left area for page copy.

Generated source files are retained under `brand/source/`. Web icons are exported from the deterministic SVG mark; the login illustration is the only model-generated raster asset used directly by the application.

## Exact prompts

### Core mark

```text
Create an original square core symbol for a product named Agent Skill Store. Show four interlocking flat modules forming an open repository entrance. The center negative space must suggest a Skill entering, being installed, and moving into a distribution path. Use a flat 2D, vector-friendly construction with a strong silhouette and generous clear space. Use only graphite #20262E, emerald #087F5B, and coral #E54B5D on a pure solid #FF00FF chroma-key background. Do not include text, letters, gradients, shadows, lighting, texture, 3D, isometric perspective, mockups, watermarks, shopping bags, robots, brains, lightning bolts, puzzle pieces, anthropomorphic characters, stacked cubes, or a composition resembling an existing technology company logo.
```

### Login illustration

```text
Create a 3:2 text-free login-page illustration for Agent Skill Store. Show flat module plates moving through an open repository gate, passing a clear validation checkpoint, and branching into several distribution destinations. Keep the composition simple and diagrammatic, place the module flow in the lower half, and preserve substantial clean space for interface copy. Use only graphite #20262E, emerald #087F5B, coral #E54B5D, off white #F5F7F6, and border neutral #D7DEDA. Do not include text, logos, watermarks, gradients, shadows, lighting effects, texture, 3D, isometric cubes, shopping bags, robots, brains, lightning bolts, puzzle pieces, or characters.
```

The first non-compliant drafts were discarded and are not distributed. The accepted login source was resized to 1536x1024, quantized to the documented five-color palette, and encoded as lossless WebP. All icon PNGs were rendered from the deterministic SVG mark rather than generated independently.

## Usage

- Use `agent-skill-store-mark.svg` when the available width is compact.
- Use `agent-skill-store-lockup.svg` on light backgrounds.
- Use `agent-skill-store-lockup-inverse.svg` on graphite or dark photographic backgrounds.
- Do not recolor individual modules or alter their relative positions.
- Keep clear space equal to at least one quarter of the mark width.

These assets are distributed under the repository's Apache-2.0 license.
