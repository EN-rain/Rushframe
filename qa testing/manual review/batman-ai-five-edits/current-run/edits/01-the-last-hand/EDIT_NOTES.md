# Batman: The Last Hand

A payoff-first vertical Batman edit built from local Rushframe AI analysis.

## AI functions used

- Local transcript/alignment: 15 segments.
- Scene analysis: 1 scene(s).
- Audio/event analysis: 5 event(s), loudness -17.4 LUFS.
- Editing-moment semantic search: moment_0001: All right, it's my turn to deal. Oh, shit. Get out of my fucking chair. Okay, after this round, Batman, calm down. Calm down, calm down, calm down. Oh my God. My superhero is here, guys. Are you happy to see Batman? Hide me up, chat. Batman in the building. I'm tired of these dealers. All the shit. It's about time I brought justice to the table. So many if I got here, You go in to fight them for me. — silence (match 0.255).
- Beat selection uses transcript meaning and model confidence rather than fixed source timestamps.
- AI analysis is stored inside the Rushframe project and survives save/reopen.

## Creative treatment

- Justice-line cold open in monochrome and grain.
- 24-seconds-earlier reset card over the project gradient.
- Escalating transcript-driven cuts with censored kinetic captions.
- Controlled glitch and spark accents rather than constant low-quality effects.
- Neural echo on Batman's entrance, animated punch-ins, and a full justice-line finale.
- All source footage and source-based overlays remain vertically centered at `PositionY=0`.
- Final duration: 12.108 seconds, 720x1280 at 30 fps.

## AI-selected beats

- **cold-open** — 24.49-26.04s, confidence 0.953: I brought justice to the table.
- **deal** — 0-1.7s, confidence 0.833: All right, it's my turn to deal.
- **conflict** — 3.48-4.92s, confidence 0.955: Get out of my fucking chair.
- **escalation** — 5.9-7.34s, confidence 0.915: Okay, after this round, Batman, calm down.
- **reveal** — 11.1-12.48s, confidence 0.95: My superhero is here, guys.
- **entrance** — 18.54-19.68s, confidence 0.979: Batman in the building.
- **resolve** — 20.5-21.58s, confidence 0.993: I'm tired of these dealers.
- **finale** — 24.16-26.04s, confidence 0.953: I brought justice to the table.
