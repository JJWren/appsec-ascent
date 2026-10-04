# hello-lab: a synthetic Lab for the Engine's end-to-end test

This is test data, not curriculum content. The Engine's hello-lab test (P28) seals these folders with a throwaway key at
test time and plays a Lab through `lab up`, `flag`, `verify` and `teachback` with the real commands.

- `module/` is the Lab Module, unpacked into the workspace on `lab up`. Its greeter has a debug backdoor.
- `plant.json` is the plant spec: the Engine writes the Flag to `throughline/.flags/hello.txt`.
- `tests/` holds the security tests (tier `earned`). They build against the Learner's code through `ThroughlineRoot`,
  and pin their own package versions because released tests build outside the repository's settings.
- `fix/` is the reference fix.
