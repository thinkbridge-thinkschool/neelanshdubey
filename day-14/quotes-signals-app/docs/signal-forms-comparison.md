# Signal Forms vs. Reactive Forms — create-a-quote

Two components implement the exact same form against the exact same real
backend:

| | Reactive Forms | Signal Forms (preview) |
|---|---|---|
| Route | `/quotes/new` | `/quotes/new-signal` |
| File | `create-quote.component.ts` | `create-quote-signal.component.ts` |
| API | `@angular/forms` (`FormGroup`, `Validators`) | `@angular/forms/signals` (`form`, `schema`, `validate`, `submit`) — `@experimental 21.0.0`/`21.2.0` in the installed `@angular/forms@21.2.21` |

Both call the same `QuoteService.createQuote()`, which does `POST
/api/quotes` with `{ author: string, text: string }` (`CreateQuoteRequest`
in `models/quote.model.ts`) and enforces the same rules the real
`Day-5/QuotesApi` backend does: both fields required after trimming,
`author` ≤ 200 chars, `text` ≤ 1000 chars (trimmed), 400 on validation
failure with an ASP.NET `errors` map.

## How the build went

This was built by briefing Claude Code with the real endpoint, the real
request/response shapes, and the real Signal Forms API surface (verified
against the installed package's `.d.ts`/`.mjs`, not guessed), then reviewing
the diff. The agent got the structure right on the first pass and all 63
tests + the production build were green — but the diff review caught one
real problem before this was called done:

**The bug caught in review:** the agent's first version read the server
error banner straight from `quoteForm().errors()`. Signal Forms ties each
field's `submissionErrors` to a `linkedSignal` keyed off that field's own
*value* — which turns out to mean an error attached to the form root (via
omitting `fieldTree`, per the framework's own docs example) also resets
the instant **any** field's value changes, since the root's value is the
whole model object. Concretely: trigger a real 401 (an expired/invalid
token), see the error banner, then type a single character into the
`author` field with no resubmit — the banner vanished immediately. The
agent's comment described this as a clean simplification with no downside.
It isn't: editing quote text does nothing to fix an expired token, so the
error disappearing on an unrelated edit is worse than the reactive
version's behavior, which correctly holds the error until the next actual
submit attempt. Sent back, the agent fixed it by managing `serverError` as
a plain signal cleared only at the start of the submit `action` — same
timing as the reactive version's `serverError.set(null)` — while still
returning the error to `submit()` so the framework's own error-tracking
stays consistent underneath. Verified fixed by re-running the exact repro
in a real browser against the real API (see Verification below).

## Where Signal Forms is simpler

- **No `viewChild`/`ElementRef` juggling for focus.** The reactive version
  needs two `viewChild<ElementRef>()` refs (`authorInput`, `textInput`)
  purely so `focusFirstInvalid()` can call `.nativeElement.focus()`.
  `FieldState.focusBoundControl()` does this for free — `quoteForm.author().focusBoundControl()`
  finds whatever DOM control is bound via `[formField]` and focuses it, so
  only one `viewChild` remains (for the plain `<p>` error banner, which
  isn't a field).
- **Submission concurrency and in-flight state come built in.**
  `quoteForm().submitting()` already reflects an in-flight `submit()` call,
  and `submit()` is itself a documented no-op (returns `false`) if a
  submission is already running — the reactive version has to hand-roll its
  own `submitting` signal and check it manually at the top of `onSubmit()`.
- **Declarative wiring for the two most mechanical parts of a form.**
  `<form [formRoot]="quoteForm">` replaces `[formGroup]` + `(ngSubmit)` +
  the `if (this.submitting()) return;` / `markAllAsTouched()` /
  `focusFirstInvalid()` dance in `onSubmit()` — `[formRoot]` calls
  `novalidate`-then-`submit()` itself, and `onInvalid` in the submission
  config is the hook for "focus the first bad field," so the corresponding
  `onSubmit()` method is now only a test hook, not something the template
  calls.
- **Server errors have a first-class attachment point.** `submit()`'s
  `action` can return `[{ kind, message, fieldTree }]` and have it merged
  straight into a specific field's `errors()` (or the form root's, by
  omitting `fieldTree`) — there's a real, documented mechanism for "the
  server rejected this," not just a component-local convention.

## Where it's still rough (preview-API growing pains)

- **The trim-before-validate gap isn't actually solved, just moved.**
  Signal Forms' built-in `required()`/`maxLength()` don't trim any more
  than classic `Validators.required`/`maxLength` do, and the real API
  trims before checking. Both versions end up writing near-identical
  custom validators by hand (`requiredTrimmed`/`maxLengthTrimmed` vs. two
  `validate()` calls per field) — Signal Forms doesn't remove this
  boilerplate, it just changes its shape.
- **The auto-clearing footgun above.** Attaching a submission error to the
  form root (the documented pattern for a "banner" error with no specific
  field) inherits a `linkedSignal` reset tied to the *whole model's* value,
  not to "did the thing that caused this error get addressed." That's an
  easy trap for an agent (or a person) to walk into and not notice, because
  it looks like it's working right up until you specifically try editing a
  field that isn't the one your error is "about."
- **No centralized error-message-by-kind mapping.** The reactive version's
  `errorMessage()` switches on `errors['required']` / `errors['maxlength']`
  and produces the copy from one place. In the Signal Forms version, each
  `validate()` call constructs its own `{ kind, message }` inline — fine
  for two fields, but it means the user-facing copy lives scattered across
  the schema instead of in one method, which won't scale as cleanly to a
  form with more fields or more validators.
- **It's `@experimental`.** Every export in `@angular/forms/signals` is
  tagged `@experimental` in the shipped `.d.ts` (21.0.0–21.2.0) — the
  surface used here (`form`, `schema`, `validate`, `submit`, `[formField]`,
  `[formRoot]`, `FieldState`) is real and works today, but Angular is
  explicit that it can still change shape before stabilizing.

## Verification

Both the client-side validators (unit tests) and the actual running app
(a real headless-browser walkthrough against the real dev server and the
real `Day-5/QuotesApi` backend, no mocked HTTP) were checked:

- **`ng test`** — 63/63 passing across 8 spec files, including the new
  `create-quote-signal.component.spec.ts` (mirrors the reactive spec's
  coverage: pristine-not-invalid, touched+invalid ARIA wiring, submit-when-invalid
  blocks the API call and moves focus, whitespace-only/over-length
  rejection, a successful POST showing the created id, 400 and 500
  failures showing the right message, and reset+refocus on "create
  another").
- **`ng build`** — clean production build, no errors/warnings.
- **Real browser walkthrough** (`a11y-audit/verify-signal-forms.mjs`,
  screenshots in `a11y-audit/screenshots-signal/`, raw notes in
  `a11y-audit/signal-forms-verification-notes.json`) — registered a
  throwaway user against the real `/api/auth/register`, then drove
  `/quotes/new-signal` with real keyboard/mouse input:

  | State | What was checked | Result |
  |---|---|---|
  | Pristine | `#author[aria-invalid]` absent, no error span | confirmed absent |
  | Dirty, still focused | typed then cleared without blurring — `aria-invalid` flips true immediately (confirmed identical on the reactive version too, not a Signal Forms quirk — this was double-checked side by side after an initial timing-artifact false negative) | matches reactive |
  | Touched, required firing | blurred an empty author field | `"Author is required."` shown |
  | Maxlength firing | 201-char author | `"Author must be 200 characters or fewer."` shown |
  | Invalid submit | valid author, empty text, `Enter` | focus moved to `#text`, no request sent |
  | Submitting | real POST delayed 900ms in flight | submit button disabled + `aria-busy` |
  | Clean submit | real POST completes | success banner shown, correct created id |
  | Create another | click after success | form reset, `#author` refocused |
  | Failed submit | real 401 from a corrupted token | `"You are not authorized to do that."` banner shown, focus moved to it |
  | **Error persistence (the bug above)** | edited `#author` after the 401 banner appeared, without resubmitting | **before fix:** banner vanished · **after fix:** banner still present, matching the reactive version |

  Every quote created during the run was deleted again via the real
  `DELETE /api/quotes/{id}` at the end of the script, the same cleanup
  convention as `a11y-audit/run-audit.mjs`.
