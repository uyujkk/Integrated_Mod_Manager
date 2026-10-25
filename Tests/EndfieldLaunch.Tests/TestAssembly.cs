// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 EndfieldBridge contributors and Integrated Mod Manager contributors.

// The launch-running check is a machine-wide named mutex. StoreAndCommandTests holds it on purpose,
// which made SaveSettings in other test classes fail when xUnit ran the classes in parallel.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
