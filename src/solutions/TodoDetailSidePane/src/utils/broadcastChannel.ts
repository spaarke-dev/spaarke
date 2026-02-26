/**
 * broadcastChannel — Communication between TodoDetailSidePane and parent (SmartToDo).
 *
 * Uses BroadcastChannel API to notify the Kanban board when data changes,
 * so it can refetch and display updated values.
 */

const CHANNEL_NAME = "spaarke-todo-detail-channel";

export const TODO_MESSAGE_TYPES = {
  TODO_SAVED: "TODO_DETAIL_SAVED",
  TODO_CLOSED: "TODO_DETAIL_CLOSED",
} as const;

let channel: BroadcastChannel | null = null;

function getChannel(): BroadcastChannel | null {
  if (channel) return channel;
  try {
    channel = new BroadcastChannel(CHANNEL_NAME);
    return channel;
  } catch {
    console.warn("[TodoDetailSidePane] BroadcastChannel not available");
    return null;
  }
}

export function sendTodoSaved(eventId: string): void {
  getChannel()?.postMessage({
    type: TODO_MESSAGE_TYPES.TODO_SAVED,
    payload: { eventId },
  });
}

export function sendTodoClosed(eventId: string): void {
  getChannel()?.postMessage({
    type: TODO_MESSAGE_TYPES.TODO_CLOSED,
    payload: { eventId },
  });
}

export function onTodoMessage(
  callback: (data: { type: string; payload: { eventId: string } }) => void
): () => void {
  const ch = getChannel();
  if (!ch) return () => {};

  const handler = (event: MessageEvent) => {
    if (event.data?.type) {
      callback(event.data);
    }
  };
  ch.addEventListener("message", handler);
  return () => ch.removeEventListener("message", handler);
}
