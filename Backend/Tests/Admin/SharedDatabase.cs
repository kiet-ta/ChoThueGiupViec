using Xunit;

// About 30 test classes (Admin, Customers, Disputes, Identity, Payouts, Ratings, Workers, Persistence) write to and count rows of the
// SAME local SQL Server database. With classes running in parallel a count such as "open disputes" or "assignments in progress" also
// sees the rows another class has seeded at that moment (AdminDashboardTests failed with "Expected 4, Actual 5"). Running the tests
// one after the other is the only fix that does not touch every class; the whole suite stays in the order of seconds.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
