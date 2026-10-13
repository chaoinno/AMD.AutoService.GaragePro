import type { ReactNode } from 'react'
import { findPublicRoute } from '../../routes/publicRoutes'

export function PageHeading({ path, children }: { path: string; children?: ReactNode }) {
  return (
    <header className="landing-page-heading">
      <div className="landing-container">
        <h1>{findPublicRoute(path)?.h1}</h1>
        {children ? <p className="landing-lead">{children}</p> : null}
      </div>
    </header>
  )
}
