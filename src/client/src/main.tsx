import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router-dom';
import router from './router.tsx';
import { CardArtProvider } from './components/card-art';
import { ClientStateProvider } from './hooks/use-client-state';
import './index.css'


createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ClientStateProvider>
      <CardArtProvider>
        <RouterProvider router={router} />
      </CardArtProvider>
    </ClientStateProvider>
  </StrictMode>
)
