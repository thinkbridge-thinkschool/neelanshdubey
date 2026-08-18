# Week 1 Reflection

## What part of this week did AI accelerate the most?

The sharpest acceleration this week was root-causing the Task 5 telemetry gap. Application Insights showed zero request data despite OpenTelemetry being fully wired in Program.cs. Rather than guessing, we cross-referenced resources.bicep against Program.cs line by line and found the mismatch immediately: the container was being given APPLICATIONINSIGHTS_CONNECTION_STRING as an environment variable, but Program.cs reads builder.Configuration["AppInsights:ConnectionString"] — a completely different key that ASP.NET Core's config binder never maps to. Renaming the bicep variable to AppInsights__ConnectionString, the double-underscore convention ASP.NET Core uses to bind nested config sections from env vars, fixed it in one azd provision cycle. What would likely have been twenty-plus minutes of trial-and-error toggling exporters or restarting the app took under five, because the diff between two specific files was inspected directly instead of guessed at.

## Where did AI suggest something subtly wrong? How did you catch it?

In Task 6, the first draft of the Polly retry and circuit-breaker logging read the failing host from args.Outcome.Result?.RequestMessage?.RequestUri?.Host. It looked reasonable and compiled cleanly. But running the actual failure test, pointing the client at an unreachable port, showed every log line printing "unknown-host" instead of the real target. On a connection-level exception there is no HttpResponseMessage, so Outcome.Result is always null and the chain short-circuits silently. The fix was args.Context.GetRequestMessage()?.RequestUri?.Host, which pulls the request off Polly's ResilienceContext directly instead of off a response that was never populated. The lesson: code that compiles and looks right isn't the same as code that has been watched fail. The bug only became visible by reading the actual log output from a forced failure, not by re-reading the code a second time.

## What competency do you feel weakest in, and what's your plan for Week 2?

Of the week's competencies — Claude Code agentic use, GitHub Copilot flow, code review etiquette, communicating tradeoffs, and not shipping what you can't explain — agentic use is where I feel weakest. Most of this week I drove Claude Code through short, single-step prompts rather than letting it own longer, multi-step chains of work. My plan for Week 2 is to deliberately study and practice with Claude Code's skills, subagents, and workflow features, so I'm using deliberate orchestration instead of ad-hoc prompting for anything with more than two or three dependent steps.

## What surprised you about the pace?

The pace was faster than I expected overall, and debugging specifically is where that showed up most. The Task 1 N+1 query fix and the Task 5 telemetry env-var mismatch are the clearest examples: both are the kind of bug that could easily eat half a day of manual log-staring, and both were found and fixed within a single focused session by pairing a real trace or log output with a direct code comparison. I went in assuming AI assistance would mostly speed up boilerplate and setup work; instead the biggest time savings showed up in root-cause debugging, which I hadn't expected going in.
