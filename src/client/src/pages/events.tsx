"use client"

import { useEffect, useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import { DataTable } from "@/components/ui/data-table";

export interface Event {
  id: string;
  name: string;
  format: string;
  players: string;
  rounds: number;
  entryFee: string;
  startTime: string;
  endTime: string;
}

function formatDate(dateString: string) {
  const date = new Date(dateString);
  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

const columns: ColumnDef<Event, keyof Event>[] = [
  {
    accessorKey: "id",
    header: "ID",
  },
  {
    accessorKey: "description",
    header: "Name",
  },
  {
    accessorKey: "format",
    header: "Format",
  },
  {
    accessorKey: "totalPlayers",
    header: "Players",
  },
  {
    accessorKey: "totalRounds",
    header: "Rounds",
  },
  {
    accessorKey: "startTime",
    header: "Start Time",
    cell: ({ row }) => formatDate(row.getValue("startTime")),
  },
  {
    accessorKey: "endTime",
    header: "End Time",
    cell: ({ row }) => formatDate(row.getValue("endTime")),
  },
  {
    accessorKey: "entryFee",
    header: "Entry Fee",
  }
];

export default function Events() {
  const [events, setEvents] = useState<Event[]>([]);

  useEffect(() => {
    const fetchEvents = async () => {
      // const response = await fetch('/api/events/geteventslist');
      // const data = await response.json();
      // setEvents(data);

      const response = await fetch('/api/events/geteventslist?stream=true');
      if (!response.ok) {
        console.error("Failed to fetch events:", response.statusText);
        return;
      }

      if (!response.body) {
        console.error("Response body is null");
        return;
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder("utf-8");
      let buffer = '';

      const processEvent = async (buffer: string) => {
        try {
          const { id, description, totalPlayers, minimumPlayers, ...props } = JSON.parse(buffer);

          const players = `${totalPlayers} / ${minimumPlayers}`;
          const event = { id, name: description, ...props, players } as Event;

          // Get the entry fee from the server
          var res = await fetch(`/api/events/getentryfee/${event.id}`);
          if (!res.ok) {
            console.error(`Failed to fetch event ${event.id}:`, res.statusText);
            return; // Skip this event if the fetch fails
          }
          event.entryFee = await res.text();

          setEvents((prevEvents) => [...prevEvents, event]);
        } catch (error) {
          console.error("Error parsing JSON:", error, "Buffer:", buffer);
        }
      }

      const processStream = async () => {
        while (true) {
          const { done, value } = await reader.read();
          if (done) {
            // Process any remaining data in the buffer
            if (buffer.trim()) await processEvent(buffer);
            break;
          }

          buffer += decoder.decode(value, { stream: true });
          const lines = buffer.split('\n');

          // Keep the last (potentially incomplete) line in the buffer
          buffer = lines.pop() || '';

          for (const line of lines) {
            if (line.trim()) await processEvent(line);
          }
        }
      }

      processStream().catch(error => {
        console.error("Error processing stream:", error);
      });

      // const idsResponse = await fetch('/api/events/geteventids');
      // if (!idsResponse.ok) {
      //   console.error("Failed to fetch event IDs:", idsResponse.statusText);
      //   // Optionally, set an error state here
      //   return;
      // }
      // const ids: number[] = await idsResponse.json();
      //
      // // Fetch `/api/events/getevent/${id}` for each ID asynchronously,
      // // preserving the order of events based on the order of eventIds.
      // setEvents([]);
      // await Promise.all(
      //   ids.map(async (id) => {
      //     try {
      //       const response = await fetch(`/api/events/getevent/${id}`);
      //       if (!response.ok) {
      //         console.error(`Failed to fetch event ${id}:`, response.statusText);
      //         return; // Skip this event and continue with the next
      //       }
      //       const event: Event = await response.json();
      //       setEvents((prevEvents) =>
      //         [...prevEvents, event]
      //           // Sort based on the index of the ID in eventIds
      //           .sort((a, b) => ids.indexOf(a.id) - ids.indexOf(b.id)));
      //     } catch (error) {
      //       console.error(`Error fetching event ${id}:`, error);
      //       // Optionally, set an error state here
      //     }
      //   })
      // );
    };

    fetchEvents();
  }, []);

  return (
    <div className="container mx-auto">
      <DataTable columns={columns} data={events} />
    </div>
  );
}
