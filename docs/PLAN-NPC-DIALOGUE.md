# NPC dialogue

1. [complete] Record the repository conventions and inspect the existing dialogue flow.
2. [complete] Extend the C# dialogue flow with explicit, independently testable choices and update its documentation.
3. [complete] Validate the browser flow and Release build.
4. [in progress] Publish on main, verify the live browser flow, and convert this plan to an activity.

Acceptance requires a dialogue that works with AI disabled, with stable choices and a way back from each branch.

Design decision. Use the existing C# scheduler and localized topic catalog.
Reuse the conversation window's wrapping row of outlined SUI buttons. This
follows the project design and the conversation's existing action buttons.

Release publish, RulesSmoke, and browser tests passed. The browser suite
covered both rendering modes. Mobile layout passed in portrait and landscape,
on desktop, and on tablet.

Next action. Push to main and rerun the dialogue scenario on GitHub Pages.
