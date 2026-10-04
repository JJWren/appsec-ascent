using Ascent.Core.Errors;
using Ascent.Maintainer.Hosting;

// ascent-maint: maintainer-only. It is never packed with, or referenced by, the Learner's Engine (SEC-U2-09).
var exitCode = await MaintainerApp.Create(new MaintainerContext()).RunAsync(args);
return exitCode < 0 ? ExitCodes.Usage : exitCode;
