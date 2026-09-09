import { useEffect, useRef, useState } from 'react'
import { cn } from '../../lib/utils'

type SignaturePadProps = {
  className?: string
  /// เรียกทุกครั้งที่เส้นเปลี่ยน — null เมื่อล้างจนว่างเปล่า
  onChange?: (hasSignature: boolean) => void
}

export type SignaturePadHandle = { toBlob: () => Promise<Blob | null>; clear: () => void; isEmpty: () => boolean }

/// กระดาน canvas จับลายเซ็นด้วยเมาส์/นิ้ว — ยังไม่มีคอมโพเนนต์นี้บนเว็บ (มีแต่ฝั่งมือถือ)
/// ใช้เฉพาะ stopgap "ยืนยันส่งมอบบนเว็บ" เท่านั้น (ดู [ASSUME] ใน JobCardModal PaymentStage)
export function SignaturePad({ className, onChange, handleRef }: SignaturePadProps & {
  handleRef?: (handle: SignaturePadHandle) => void
}) {
  const canvasRef = useRef<HTMLCanvasElement>(null)
  const drawing = useRef(false)
  const hasInk = useRef(false)
  const [empty, setEmpty] = useState(true)

  const context = () => canvasRef.current?.getContext('2d') ?? null

  const resize = () => {
    const canvas = canvasRef.current
    if (!canvas) return
    const ratio = window.devicePixelRatio || 1
    const rect = canvas.getBoundingClientRect()
    canvas.width = rect.width * ratio
    canvas.height = rect.height * ratio
    const ctx = context()
    if (!ctx) return
    ctx.scale(ratio, ratio)
    ctx.lineWidth = 2.2
    ctx.lineCap = 'round'
    ctx.lineJoin = 'round'
    ctx.strokeStyle = '#1a1a1a'
  }

  useEffect(() => {
    resize()
    window.addEventListener('resize', resize)
    return () => window.removeEventListener('resize', resize)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    handleRef?.({
      toBlob: () =>
        new Promise((resolve) => {
          const canvas = canvasRef.current
          if (!canvas || !hasInk.current) { resolve(null); return }
          canvas.toBlob((blob) => resolve(blob), 'image/png')
        }),
      clear: () => {
        const canvas = canvasRef.current
        const ctx = context()
        if (canvas && ctx) ctx.clearRect(0, 0, canvas.width, canvas.height)
        hasInk.current = false
        setEmpty(true)
        onChange?.(false)
      },
      isEmpty: () => !hasInk.current,
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const point = (event: React.PointerEvent<HTMLCanvasElement>) => {
    const rect = event.currentTarget.getBoundingClientRect()
    return { x: event.clientX - rect.left, y: event.clientY - rect.top }
  }

  const start = (event: React.PointerEvent<HTMLCanvasElement>) => {
    event.currentTarget.setPointerCapture(event.pointerId)
    drawing.current = true
    const ctx = context()
    const { x, y } = point(event)
    ctx?.beginPath()
    ctx?.moveTo(x, y)
  }

  const move = (event: React.PointerEvent<HTMLCanvasElement>) => {
    if (!drawing.current) return
    const ctx = context()
    const { x, y } = point(event)
    ctx?.lineTo(x, y)
    ctx?.stroke()
    if (!hasInk.current) { hasInk.current = true; setEmpty(false); onChange?.(true) }
  }

  const end = () => { drawing.current = false }

  return (
    <div className={cn('signature-pad', className)}>
      <canvas
        ref={canvasRef}
        className="signature-pad__canvas"
        onPointerDown={start}
        onPointerMove={move}
        onPointerUp={end}
        onPointerLeave={end}
      />
      {empty ? <span className="signature-pad__placeholder">เซ็นในกรอบนี้</span> : null}
    </div>
  )
}
