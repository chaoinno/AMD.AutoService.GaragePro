import { Toaster as SonnerToaster, type ToasterProps } from 'sonner'
import { CheckCircle2, CircleAlert, Info, LoaderCircle, TriangleAlert } from 'lucide-react'

export function Toaster(props: ToasterProps) {
  return (
    <SonnerToaster
      position="top-right"
      richColors
      icons={{
        success: <CheckCircle2 aria-hidden="true" />,
        error: <CircleAlert aria-hidden="true" />,
        info: <Info aria-hidden="true" />,
        warning: <TriangleAlert aria-hidden="true" />,
        loading: <LoaderCircle className="spin" aria-hidden="true" />,
      }}
      toastOptions={{ duration: 3_500 }}
      {...props}
    />
  )
}
