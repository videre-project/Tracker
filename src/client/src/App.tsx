/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useState } from 'react';
import './App.css';


interface Event {
  id: number;
  name: string;
  format: string;
  startTime: string;
  minPlayers: number;
  maxPlayers: number;
  players: number;
  rounds: number;
  IsCompleted: boolean;
}

function App() {
  const [eventsList, setEventsList] = useState<Event[]>();

  async function populateEventList() {
    const response = await fetch('/api/events/geteventslist');
    const data = await response.json();
    setEventsList(data);
  }

  useEffect(() => {
    populateEventList();
  }, []);

  const handleOpenEvent = async (id: number) => {
    await fetch(`/api/events/openevent/${id}`);
  };

  const contents = eventsList === undefined
    ? <p><em>Loading... Please refresh once the ASP.NET backend has started. See <a href="https://aka.ms/jspsintegrationreact">https://aka.ms/jspsintegrationreact</a> for more details.</em></p>
    : <table className="table table-striped" aria-labelledby="tabelLabel">
      <thead>
        <tr>
          <th>Date</th>
          <th>Name</th>
          <th>Format</th>
          <th>Players</th>
          <th>Rounds</th>
        </tr>
      </thead>
      <tbody>
        {eventsList.map(event =>
          <tr key={event.id}>
            <td>{new Date(event.startTime).toLocaleString(undefined, {
              month: 'numeric',
              day: 'numeric',
              hour: 'numeric',
              minute: 'numeric',
              hour12: true
            })}</td>
            <td>{event.name}</td>
            <td>{event.format}</td>
            <td>{event.players} / {event.minPlayers}</td>
            <td>{event.rounds}</td>
            <td><button onClick={() => handleOpenEvent(event.id)}>Open Event</button></td>
          </tr>)}
      </tbody>
    </table>;

  return (
    <div>
      <h1 id="tabelLabel">Event List</h1>
      <p>This component demonstrates fetching data from the MTGO client.</p>
      {contents}
    </div>
  );
}

export default App;