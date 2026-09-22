// PCSuite.Elevated — placeholder until Task 005 (Elevation Framework).
//
// Security rules for this executable:
// - It must never expose a generic shell or arbitrary command execution.
// - It will accept only a small allow-list of structured operations over
//   authenticated local IPC (named pipes), validated against a schema.
// - It must run short-lived and exit after completing its operation.

Console.Error.WriteLine("PCSuite.Elevated cannot be executed directly.");
Console.Error.WriteLine("Elevated operations are implemented in Task 005 (IElevationManager + IPC).");
return 2;
