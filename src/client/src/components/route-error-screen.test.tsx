import { cleanup, fireEvent, render, screen } from "@testing-library/react"
import type { ButtonHTMLAttributes, ReactNode } from "react"
import { Link, Outlet, RouterProvider, createMemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { RouteErrorScreen } from "./route-error-screen"

vi.mock("@videreproject/ui", () => ({
  Button: ({ asChild, children, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { asChild?: boolean; size?: string; variant?: string }) =>
    asChild ? children : <button {...props}>{children}</button>,
}))

function BrokenPage(): ReactNode {
  throw new Error("Fixture render failure")
}

function Shell() {
  return <><nav aria-label="App navigation"><Link to="/">Dashboard</Link></nav><Outlet /></>
}

describe("Route error screen", () => {
  const expectedError = (event: ErrorEvent) => {
    if (event.message === "Fixture render failure") event.preventDefault()
  }
  beforeEach(() => {
    vi.spyOn(console, "error").mockImplementation(() => {})
    window.addEventListener("error", expectedError)
  })
  afterEach(() => {
    cleanup()
    window.removeEventListener("error", expectedError)
  })

  it("keeps navigation and the stack visible, and allows leaving the failed page", async () => {
    const router = createMemoryRouter([{
      path: "/", element: <Shell />, children: [
        { index: true, element: <h1>Dashboard content</h1> },
        { path: "broken", element: <BrokenPage />, errorElement: <RouteErrorScreen /> },
      ],
    }], { initialEntries: ["/broken"] })
    render(<RouterProvider router={router} />)

    expect(await screen.findByRole("heading", { name: "Unable to display this page" })).toBeVisible()
    expect(screen.getByRole("navigation", { name: "App navigation" })).toBeVisible()
    expect(screen.getByRole("button", { name: "Reload page" })).toBeVisible()
    const stack = screen.getByLabelText("Error stack trace")
    expect(stack).toHaveTextContent("Error: Fixture render failure")
    expect(stack).toBeVisible()
    expect(screen.getByRole("heading", { name: "Stack trace" })).toBeVisible()
    fireEvent.click(screen.getByRole("link", { name: "Go to dashboard" }))
    expect(await screen.findByRole("heading", { name: "Dashboard content" })).toBeVisible()
    expect(screen.queryByRole("heading", { name: "Unable to display this page" })).not.toBeInTheDocument()
  })

  it("gives an unknown address a useful root fallback", async () => {
    const router = createMemoryRouter([{
      path: "/", element: <h1>Dashboard content</h1>, errorElement: <RouteErrorScreen standalone />,
    }], { initialEntries: ["/missing"] })
    render(<RouterProvider router={router} />)

    expect(await screen.findByRole("heading", { name: "Page not found" })).toBeVisible()
    expect(screen.queryByRole("button", { name: "Reload page" })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole("link", { name: "Go to dashboard" }))
    expect(await screen.findByRole("heading", { name: "Dashboard content" })).toBeVisible()
  })
})
