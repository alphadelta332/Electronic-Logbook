# Product UI consistency

For mobile UI changes, including redesigns:

1. Before editing, find the same action or interaction on other screens. Name the
   incumbent component and callers in the task update. Reuse its component, icon,
   state feedback, theme tokens, and interaction unless the task requires a difference.
2. When repeated controls have drifted, fix their shared implementation. The filter
   trigger is `Pages/FilterToggleButton.razor`; Entries, Totals, and Charts use it.
   A visual reference supplies direction within the app's existing control patterns.
   Use "Filters" with the active count; the page already supplies its scope. Date
   ranges share `.date-range-fields` and Start/End labels. MudTabs view selectors
   use `.segmented-tabs`, which shares the native segmented-control style rules.
3. Validate the changed screen alongside an existing caller, including collapsed,
   expanded, selected, cleared, and applicable error states. Check light/dark themes,
   large text, and 48px touch targets. Component tests cover shared wiring and state;
   screenshots establish rendered appearance. Follow AGENTS.md's phone UI restrictions.
4. Report which comparisons ran and which remain unverified. Record unresolved
   inconsistencies in TODO.md. Passing functional or accessibility checks alone
   does not establish consistency across screens.

Ponytail's reuse rule supports this workflow. It is agent guidance, not an automatic
design checker. Shared components and regression tests enforce the known contracts;
review across screens must identify new ones. Use context-appropriate differences
when the actions differ, and explain the reason rather than making every button alike.
