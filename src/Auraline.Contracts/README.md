# Auraline.Contracts

This dependency-light .NET 8 project contains contracts that genuinely cross the Auraline Host and plugin boundary. M1 introduces only `ContractVersion` with major-version compatibility semantics.

Provider-internal models, rendering details, Windows dependencies, and Host configuration remain outside this project. Functional plugin negotiation begins later.
