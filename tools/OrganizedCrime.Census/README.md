# OC-30 temporary runtime census harness

This project is intentionally separate from `tools/OrganizedCrime` production
runtime wiring. It subscribes only to the six named `S1API.Lifecycle.GameLifecycle`
events and the three named `TimeManager` events. It logs bounded JSON rows and
performs no setters, patches, model calls, sidecar writes, save/load calls,
police hooks, or gameplay mutations.

Time-event subscriptions are detached at preload and scene-change boundaries
and rebound from the current `TimeManager` at load complete. The bounded census
buffer holds 4096 rows and emits a one-time warning if that capacity is reached.

Build output is isolated at:

`artifacts/oc-30-runtime-census/OrganizedCrime.Census.dll`

The owner must not install or launch it until providing separate authorization.
