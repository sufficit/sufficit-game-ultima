# NPC dialogue

Approach the Garden Guardian or the Wanderer and open the conversation with
**E**, the controller's action button, or the on-screen **A** button. Select
an available topic to see the question and the character's response in the
transcript. You can then choose another topic or close the window.

The Guardian explains what to do in the garden, how to collect materials,
and how to light the entrance. The Wanderer offers directions. These topics
work without an AI model or a game API connection. Selecting a topic about
lighting displays instructions. Use **Iluminar o jardim** to request a world
edit. Authored topic turns stay in the open transcript. The Journal retains
free-text replies through the existing history pipeline.

## Add a topic

Add an entry to the corresponding character in
[DialogTopics.cs](../src/Npc/DialogTopics.cs). Keep its ID stable and unique
and use the player's locale for the question and reply. The
[dialogue brain](../src/Npc/DialogBrain.cs) checks the active character and
whether a free-text reply is pending before accepting the event. The
declarative window renders the available choices with Sufficit.Blazor.UI.

Run RulesSmoke for both characters' topic paths and `declarative-windows`
for keyboard and pointer selection in the browser.
