# Signal actionability consistency correction

- Signal browsing and dashboard active counts now include only observations inside the configured
  actionability window instead of every historical row whose stored `IsActive` flag is true.
- Manual entry uses the same central policy and rejects future-dated signals as well as expired
  signals before recommendation sizing or broker access.
- The lifetime is configured by `SignalLifecycle:ActionableLifetimeHours` and defaults to 24 hours.
- Historical signal rows are retained unchanged for audit.

This intentionally reduces active-signal counts when old rows were previously presented as current
opportunities.
