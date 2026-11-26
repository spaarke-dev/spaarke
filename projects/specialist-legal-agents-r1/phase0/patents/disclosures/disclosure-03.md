---
status: test
disclosureId: disclosure-03
sourcePublication: US 7,469,381 B2
sourceTitle: List scrolling and document translation, scaling, and rotation on a touch-screen display
sourceGranted: 2008-12-23
sourceRecord: public (granted US patent)
inventionType: software / computer-implemented (touch-screen user interface)
attorneyReviewed: false
---

# Test disclosure 03: showing the user they have reached the end of a list on a touch screen

> Test content for Phase 0 (spec FR-09). This is a short invention summary written for testing from the public record of the patent named above. It is not the inventors' text and does not reproduce the patent's claims. Draft from this disclosure only; do not look up or recall the published claims.

## Problem

On a small touch-screen device the user scrolls a list (contacts, messages, songs) or moves a document by dragging a finger across it. When the end of the list is reached, the content simply stops. The user cannot tell whether the end has been reached or the device has stopped responding, and keeps dragging.

## The idea

Let the content move past its end while the finger is still on the screen, and then pull it back when the finger lifts:
1. The device detects a finger (or other object) touching the screen over a list of items, and scrolls the list in the direction the finger moves.
2. If the end of the list is reached while the finger is still in contact and still moving, the device keeps moving the list, so an area beyond the last item comes into view.
3. When the device detects that the finger has left the screen, it scrolls the list back the other way until the area beyond the end is no longer shown and the last item sits at the edge of the display.

The area beyond the end makes the end visible, and the pull-back feels like an elastic band, so the user knows at once that the end was reached.

## Related behaviours described

- **Momentum:** after a quick swipe the list keeps scrolling once the finger has lifted and slows down, like a physical object with friction (damped motion).
- **Documents:** the same overshoot and return applies when an electronic document (for example a web page) is dragged past one of its edges: the area beyond the edge is shown while contact continues, and the document moves back once contact ends.
- **Scaling and rotation:** zoom gestures scale the document by an amount the user chooses; a multi-finger twisting gesture rotates the displayed content.

## Device

A portable electronic device with a touch-sensitive display, one or more processors, memory, and one or more programs stored in the memory that, when run by the processors, carry out the behaviour above. The same programs can be supplied on a storage medium.

## What the inventor considers new

On a touch-screen display, moving content past its terminus while contact continues so that an area beyond the terminus is shown, and returning the content in the opposite direction once contact ends until that area is no longer shown.
