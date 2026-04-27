# CrawlSharp Improvements Plan

## Scope

This plan covers the CrawlSharp changes needed to support automatic expansion of collapsible content in headless-browser crawls, to document the existing rendered-HTML behavior clearly, and to ship the work as a patch release.

This plan is based on the current source tree in `C:\Code\CrawlSharp`, not just the published package.

## Current State

- Headless crawling already returns rendered HTML, not stripped text.
  - In `src/CrawlSharp/Web/WebCrawler.cs`, `RetrieveWithPlaywright()` navigates with `page.GotoAsync(...)` and then captures `page.ContentAsync()`.
  - The returned HTML is serialized into `WebResource.Data`.
- Non-navigable assets such as PDFs already bypass Playwright and are fetched with the REST client path.
- There is currently no way to interact with the page before `page.ContentAsync()` is captured.
  - No click pass.
  - No `details` expansion.
  - No accordion handling.
  - No post-load settle delay beyond `WaitUntilState.Load`.
- Link discovery already runs against the returned HTML bytes.
  - If expansion happens before capture, links revealed by accordions can also be discovered.

## Goals

1. Add a default-on way to auto-expand common collapsible UI patterns before HTML capture, with an explicit opt-out.
2. Keep the current output contract intact.
   - `WebResource.Data` remains `byte[]`.
   - `/crawl` remains the same endpoint.
   - No breaking schema changes.
3. Clarify in docs and examples that headless mode already returns rendered full HTML.
4. Ship as a patch release.

## Non-Goals

- Do not add generic arbitrary-page scripting from API callers in this change.
  - That is powerful but expands security and support surface significantly.
- Do not change the crawl response format.
- Do not change link-following semantics beyond the natural effect of expanded DOM content.
- Do not attempt a full browser automation framework inside CrawlSharp.

## Proposed Public API Changes

Add the following properties to `src/CrawlSharp/Web/CrawlSettings.cs`:

- `AutoExpandCollapsibles` (`bool`, default `true`)
  - Master switch for DOM interaction prior to capture.
  - Headless-only. Ignored when `UseHeadlessBrowser == false`.
  - Acts as an opt-out kill switch for callers that need pre-change behavior.
- `PostLoadDelayMs` (`int`, default `0`)
  - Optional delay after `page.GotoAsync(...)` completes and before any expansion logic runs.
  - Useful for pages that finish `load` before client-side hydration completes.
- `PostInteractionDelayMs` (`int`, default `250`)
  - Delay after each expansion pass to allow the DOM to settle.
- `MaxExpansionPasses` (`int`, default `2`)
  - Number of interaction passes to attempt before capture.
- `ExpansionSelectors` (`List<string>`, default empty)
  - Additional caller-supplied CSS selectors to click during expansion.
  - These are additive to the built-in selector set.

Why these settings:

- `AutoExpandCollapsibles` keeps expansion enabled by default while still giving callers a clean escape hatch.
- `PostLoadDelayMs` is safer than globally changing navigation from `WaitUntilState.Load` to `NetworkIdle`.
- `PostInteractionDelayMs` and `MaxExpansionPasses` make the behavior deterministic and tunable.
- `ExpansionSelectors` gives targeted escape hatches without exposing arbitrary script injection.

## Implementation Plan

### 1. Extend `CrawlSettings`

File:

- `src/CrawlSharp/Web/CrawlSettings.cs`

Changes:

- Add backing fields, properties, validation, XML docs, and defaults for:
  - `AutoExpandCollapsibles`
  - `PostLoadDelayMs`
  - `PostInteractionDelayMs`
  - `MaxExpansionPasses`
  - `ExpansionSelectors`
- Validation rules:
  - `PostLoadDelayMs >= 0`
  - `PostInteractionDelayMs >= 0`
  - `MaxExpansionPasses >= 1`
  - `ExpansionSelectors` null-coalesces to empty list

### 2. Add Playwright expansion helpers

File:

- `src/CrawlSharp/Web/WebCrawler.cs`

Add private helpers near the Playwright code path:

- `Task DelayIfNeeded(int delayMs, CancellationToken token)`
- `Task ExpandCollapsibleContent(IPage page, Uri normalizedUri, CancellationToken token)`
- `Task<int> ExpandBuiltInTargets(IPage page, CancellationToken token)`
- `Task<int> ExpandCustomSelectors(IPage page, CancellationToken token)`

Implementation details:

- Run expansion only when:
  - `UseHeadlessBrowser == true`
  - `AutoExpandCollapsibles == true`
- Sequence inside `RetrieveWithPlaywright()`:
  1. `page.GotoAsync(...)`
  2. Optional `PostLoadDelayMs`
  3. Expansion loop for `MaxExpansionPasses`
  4. Optional `PostInteractionDelayMs` between passes
  5. `page.ContentAsync()`

Built-in expansion behavior:

- Open all `<details>` elements by setting `open = true`.
- Click elements matching a conservative built-in selector list such as:
  - `summary`
  - `[aria-expanded="false"][aria-controls]`
  - `[data-bs-toggle="collapse"]`
  - `[data-toggle="collapse"]`
  - `.accordion-button.collapsed`
  - `[role="button"][aria-controls][aria-expanded="false"]`

Guardrails:

- Skip elements that are disabled.
- Skip hidden elements when Playwright can determine non-visibility.
- Scroll targets into view before clicking.
- Use no-navigation clicks only.
- Swallow per-element click failures and log them at debug level.
- Stop early if a pass makes zero changes.

Important design point:

- Do not click generic `a[href]` or unbounded button selectors.
  - The change must remain narrowly focused on expansion, not general interaction.
  - This matters more because the behavior is default-on.

### 3. Keep rendered HTML behavior unchanged, but verify it

File:

- `src/CrawlSharp/Web/WebCrawler.cs`

Code changes:

- No contract change required.
- Keep `page.ContentAsync()` as the capture mechanism after any expansion pass.

Documentation changes:

- Make it explicit that:
  - headless mode returns rendered DOM HTML,
  - non-headless mode returns server response bytes,
  - non-navigable assets such as PDFs still use the REST client path.

### 4. Make link discovery benefit from expanded DOM

Files:

- `src/CrawlSharp/Web/WebCrawler.cs`

Code changes:

- None beyond running expansion before `page.ContentAsync()`.
- `ExtractLinksFromHtml(...)` already runs on captured HTML.

Expected result:

- Links inside accordion or `details` content become discoverable when expansion is enabled.

### 5. Logging

Files:

- `src/CrawlSharp/Web/WebCrawler.cs`

Add debug-level log statements for:

- headless expansion enabled/disabled
- configured pass count
- configured extra selectors count
- per-pass click/open counts
- early exit when no more expandable elements are found

Do not log:

- full DOM
- raw page content
- sensitive authentication values

## Exact File Changes

### Core library

- `src/CrawlSharp/Web/CrawlSettings.cs`
  - Add new settings and XML docs.
- `src/CrawlSharp/Web/WebCrawler.cs`
  - Add expansion helpers.
  - Invoke them inside `RetrieveWithPlaywright()`.

### Server

- `src/CrawlSharp.Server/Program.cs`
  - No route changes required.
  - Verify the new settings deserialize cleanly through the existing `Settings` JSON payload.

### Dashboard

- `dashboard/src/views/NewCrawlView.jsx`
  - Add controls for:
    - `AutoExpandCollapsibles`
    - `PostLoadDelayMs`
    - `PostInteractionDelayMs`
    - `MaxExpansionPasses`
    - `ExpansionSelectors`
  - Add hints that these only apply when `UseHeadlessBrowser` is enabled.
- `dashboard/src/utils/api.js`
  - Include the new fields in `buildSettingsPayload(config)`.
  - Add parsing for newline-delimited `ExpansionSelectors`.
- `dashboard/dist/...`
  - Rebuild the dashboard if built assets are checked in.

## Testing Plan

### Test Strategy

The existing `src/Test/Program.cs` interactive harness is not sufficient coverage for this change. Keep it for manual inspection, but add automated, repeatable tests.

### 1. Add a real automated test project

Recommended new project:

- `src/Test.CrawlSharp/Test.CrawlSharp.csproj`

Recommended stack:

- `xUnit`
- `Microsoft.NET.Test.Sdk`
- `xunit.runner.visualstudio`

Reason:

- DOM interaction behavior is too easy to regress silently.
- This change needs deterministic pass/fail automation.

### 2. Add a local fixture server for tests

Serve small HTML pages from an in-process local HTTP server during tests.

Test fixtures should cover:

- static page
- JS-hydrated page
- page with `<details>`
- page with ARIA accordion
- page with collapsed links
- downloadable PDF route

### 3. Required automated tests

#### Rendered HTML verification

- `Headless_ReturnsRenderedHtml_AfterClientRendering`
  - Local page injects DOM text after load.
  - Assert `WebResource.Data` contains the injected HTML/text.

#### Expansion behavior

- `Headless_DetailsContent_IsExpanded_WhenAutoExpandEnabled`
  - Page contains closed `<details>` with hidden text.
  - Assert hidden text appears only when `AutoExpandCollapsibles == true`.

- `Headless_AriaAccordion_IsExpanded_WhenAutoExpandEnabled`
  - Page contains a button with `aria-expanded="false"` controlling hidden content.
  - Assert content appears in captured HTML.

- `Headless_CustomExpansionSelectors_AreApplied`
  - Page uses a non-standard class for collapse toggle.
  - Assert caller-provided selector triggers expansion.

- `Headless_ExpansionDisabled_PreservesOptOutBehavior`
  - Same fixture as above.
  - Assert content remains collapsed when `AutoExpandCollapsibles == false`.

- `Headless_DefaultExpansion_RevealedLinks_AreDiscovered`
  - Hidden content contains an anchor.
  - Assert the child URL is crawled by default in headless mode and is not crawled when `AutoExpandCollapsibles == false`.

#### Asset path verification

- `PdfRoute_FallsBackToRestClient_AndReturnsBinaryBytes`
  - Assert a PDF response is not routed through Playwright HTML capture.

### 4. Manual verification updates

Update `src/Test/Program.cs` to support a quick manual smoke test path:

- add prompts for the new settings
- log whether expansion is enabled
- optionally print the first portion of captured HTML

This is secondary to automated tests.

## Postman Updates

File:

- `CrawlSharp.postman_collection.json`

Required changes:

- Update the existing `/crawl` example payload to include the new fields in the `Crawl` object.
- Add at least one dedicated example named along the lines of:
  - `Crawl with headless auto-expand`

Suggested example payload:

```json
{
  "Authentication": {
    "Type": "None"
  },
  "Crawl": {
    "UserAgent": "CrawlSharp",
    "StartUrl": "https://example.com",
    "UseHeadlessBrowser": true,
    "AutoExpandCollapsibles": true,
    "PostLoadDelayMs": 500,
    "PostInteractionDelayMs": 250,
    "MaxExpansionPasses": 2,
    "ExpansionSelectors": [
      ".faq-toggle"
    ]
  }
}
```

Also update request descriptions to clarify:

- rendered HTML is already returned for headless navigable pages
- expansion is headless-only

## README Updates

File:

- `README.md`

Required updates:

### 1. `New in v1.0.x`

Replace the generic bullets with release-specific bullets for the new patch.

### 2. Crawl settings table

Add rows for:

- `AutoExpandCollapsibles`
- `PostLoadDelayMs`
- `PostInteractionDelayMs`
- `MaxExpansionPasses`
- `ExpansionSelectors`

### 3. New section: rendered HTML behavior

Add a short section after the settings table explaining:

- In headless mode, CrawlSharp captures the rendered DOM via Playwright.
- The captured HTML is stored in `WebResource.Data`.
- For non-navigable assets such as PDFs, CrawlSharp uses direct HTTP retrieval instead.

### 4. New section: auto-expand behavior

Document:

- what the feature is for
- that it is enabled by default in headless mode
- how to opt out with `AutoExpandCollapsibles = false`
- that it only applies in headless mode
- supported built-in patterns
- the meaning of `ExpansionSelectors`
- the fact that over-broad selectors can cause unintended clicks

### 5. Embedded example

Add a second example showing headless crawl plus auto-expand settings.

## REST API Doc Updates

File:

- `REST_API.md`

Required updates:

- Update the `/crawl` request JSON example to include the new fields.
- Add a short note that when headless mode is enabled, `WebResource.Data` contains rendered HTML for navigable pages.
- Add a short note that `AutoExpandCollapsibles` is enabled by default for headless crawls and ignored unless `UseHeadlessBrowser` is enabled.
- Fix the SSE terminator example to use `[DONE]` so the docs match the current server implementation.

## CHANGELOG Updates

File:

- `CHANGELOG.md`

Change from the current placeholder format to a concrete patch entry.

Recommended structure:

```md
# Change Log

## v1.0.22

- Added optional automatic expansion of common collapsible content during headless browser crawls
- Added tunable headless post-load and post-interaction delays
- Added configurable expansion pass count and custom expansion selectors
- Clarified rendered HTML capture behavior in documentation
- Added automated coverage for headless rendered HTML and expansion behavior

## Previous Versions

### v1.0.21

- Initial release
- Added support for headless browser crawling
- Added retry with exponential backoff on HTTP 429 responses
```

The exact previous-version bullets can be adjusted to match what was actually released, but the new patch entry should be explicit.

## Versioning

Current source version:

- `src/CrawlSharp/CrawlSharp.csproj` currently shows `<Version>1.0.21</Version>`

Required change:

- bump to `1.0.22`

Also update:

- `<PackageReleaseNotes>` in `src/CrawlSharp/CrawlSharp.csproj`
  - replace `Initial release` with a brief summary of the patch

No major or minor version bump is needed because:

- the API change is additive
- the public API shape remains additive even though the default headless output becomes richer
- no payload fields were removed or renamed

Important note:

- Although the public API remains additive, this is still a behavioral change because existing headless callers will receive expanded DOM content by default.
- If the project wants to treat default-output changes as semver-significant, a minor bump would also be defensible.
- This plan keeps the requested patch bump and makes the behavioral change explicit in docs and tests.

## Acceptance Criteria

- Existing headless callers receive expanded DOM content by default.
- Setting `AutoExpandCollapsibles = false` restores the pre-change behavior.
- Headless crawls capture revealed collapsible content by default.
- Expanded links can be discovered by follow-link crawling.
- PDFs still route through the direct HTTP path.
- README, REST API docs, and Postman all show the new fields.
- Dashboard exposes the new settings.
- Package version is bumped from `1.0.21` to `1.0.22`.

## Recommended Delivery Order

1. Add `CrawlSettings` fields.
2. Implement expansion helpers in `WebCrawler`.
3. Add automated tests.
4. Update dashboard form and payload builder.
5. Update Postman, `REST_API.md`, `README.md`, and `CHANGELOG.md`.
6. Bump version to `1.0.22`.
7. Build package and run full smoke tests.

## Risks and Mitigations

- Risk: over-broad click logic triggers navigation or changes output unexpectedly for existing headless callers.
  - Mitigation: conservative built-in selectors, no generic anchor clicks, and an immediate opt-out via `AutoExpandCollapsibles = false`.
- Risk: some frameworks need an extra hydration delay.
  - Mitigation: `PostLoadDelayMs`.
- Risk: some sites need custom selectors.
  - Mitigation: `ExpansionSelectors`.
- Risk: DOM never stabilizes.
  - Mitigation: fixed `MaxExpansionPasses` and bounded delays.
