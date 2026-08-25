# Auraline.Contracts

This dependency-light .NET 8 project contains contracts that genuinely cross the Auraline Host and consumer boundary. M3 adds render-session negotiation, expiring consumer leases, and platform-neutral frame publication/reading contracts to `ContractVersion`.

Provider-internal models, Windows memory-mapped-file types, InfoPanel types, and Host configuration remain outside this project. Functional InfoPanel integration begins in M4.
