# UUID v4 for player identity

## Context

Ultima creates one persistent player UUID per browser profile. Game sessions,
Genius conversations, and telemetry use it to associate records with the
same player across reconnects and room changes. These lookups use equality.

## Decision

Use UUID v4 for player identity. Browser-generated v4 identifiers use random
bits without encoding when the player ID was created. UUID v7 places a
millisecond creation timestamp in the identifier. Time ordering offers no
benefit for the current player identity lookups. Keep existing UUIDs stable
so Genius conversations and telemetry continue to reference the same player.
[RFC 9562, UUID v4 and v7](https://www.rfc-editor.org/rfc/rfc9562.html#section-5.4)

## When to revisit event IDs

Telemetry events have their own IDs and carry the player UUID separately.
The current collector uses event IDs for deduplication and keeps a bounded
snapshot on disk. If telemetry moves to a database that indexes many events
in insertion order, consider UUID v7 for new event IDs. Time-ordered IDs
improve index locality because newly generated keys tend to be close
together. The timestamp represents ID generation. Keep a separate event
timestamp if the occurrence time is needed. Existing player UUIDs and their
associations stay valid. [RFC 9562, sorting and database considerations](https://www.rfc-editor.org/rfc/rfc9562.html#section-6.11)
