# One healthcare Throughline System across every archetype

Every Lab hardens the same fictional telehealth product, the Throughline System. It is made up of:
- an ASP.NET Core API and web front-end
- Azure Functions and Service Bus workers
- containers
- a WPF back-office client
- an LLM patient assistant that calls tools over MCP
- a patient-message triage model served through ONNX

Using one product means vulnerabilities and fixes build on each other across Domains. For example, a design flaw introduced in D4 becomes the incident handled in D7. The product also mirrors the full range of systems the author works on day to day. It gives Domain 3 a rich compliance surface: HIPAA/HITECH, GDPR, PCI DSS for copays, and the EU AI Act.

The trade-off is that every Lab depends on the Throughline System's shared architecture, so changing that architecture later is costly.

## Considered Options
- **A separate mini-app for each archetype.** Rejected: no story that builds across Domains, and the skills transfer less well to real systems.
- **A fintech/payments product.** The author chose healthcare instead.
