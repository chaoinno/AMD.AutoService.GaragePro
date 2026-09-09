import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { cn } from '../../lib/utils'
import { Input } from './input'

export type ComboboxOption = { value: string; label: string; description?: string }

type ComboboxProps = {
  id?: string
  ariaLabel?: string
  placeholder?: string
  disabled?: boolean
  options: ComboboxOption[]
  loading?: boolean
  loadingLabel?: string
  emptyLabel?: string
  query: string
  onQueryChange: (query: string) => void
  /// ข้อความที่แสดงตอนไม่ได้โฟกัสและยังไม่ได้พิมพ์ค้นหาใหม่ — ใช้แสดงตัวเลือกที่เลือกไว้แล้ว
  selectedLabel?: string
  onSelect: (option: ComboboxOption) => void
  className?: string
}

/// ช่องค้นหาแบบเลือกได้ในตัว (พิมพ์กรอง + ลูกศร/Enter เลือก + Escape ปิด) แทนคู่ Input+Select เดิม
/// ไม่พึ่ง dependency ภายนอก (ไม่มี jQuery/select2 ในสแตกนี้) — ผู้เรียกควบคุม query (ยิง API ค้นหาเอง)
/// และ onSelect (ตัดสินใจว่าจะเซ็ตค่าเดียวไว้ค้างหรือเพิ่มแถวแล้วล้างช่องเพื่อค้นต่อ)
export function Combobox({
  id, ariaLabel, placeholder, disabled, options, loading, loadingLabel = 'กำลังค้นหา…',
  emptyLabel = 'ไม่พบรายการที่ค้นหา', query, onQueryChange, selectedLabel, onSelect, className,
}: ComboboxProps) {
  const [open, setOpen] = useState(false)
  const [highlight, setHighlight] = useState(0)
  const rootRef = useRef<HTMLDivElement>(null)
  const blurTimer = useRef<ReturnType<typeof setTimeout>>(undefined)
  const listId = `${id ?? 'combobox'}-listbox`

  useEffect(() => { setHighlight(0) }, [options])
  useEffect(() => () => clearTimeout(blurTimer.current), [])

  const select = (option: ComboboxOption) => {
    onSelect(option)
    onQueryChange('')
    setOpen(false)
  }

  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setOpen(true)
      setHighlight((h) => Math.min(h + 1, Math.max(options.length - 1, 0)))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setHighlight((h) => Math.max(h - 1, 0))
    } else if (event.key === 'Enter') {
      if (open && options[highlight]) { event.preventDefault(); select(options[highlight]) }
    } else if (event.key === 'Escape') {
      setOpen(false)
      event.currentTarget.blur()
    }
  }

  const displayValue = query || (!open ? selectedLabel ?? '' : '')

  return (
    <div className={cn('ui-combobox', className)} ref={rootRef}>
      <Input
        id={id}
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-label={ariaLabel}
        autoComplete="off"
        disabled={disabled}
        placeholder={placeholder}
        value={displayValue}
        onFocus={(event) => { setOpen(true); event.target.select() }}
        onBlur={() => {
          // หน่วงให้ mousedown ของตัวเลือกทำงานก่อน (mousedown ยิงก่อน blur) ตัวเลือกเองก็ preventDefault ไว้แล้ว
          blurTimer.current = setTimeout(() => { setOpen(false); onQueryChange('') }, 120)
        }}
        onChange={(event) => { onQueryChange(event.target.value); setOpen(true) }}
        onKeyDown={onKeyDown}
      />
      {open && !disabled ? (
        <ul className="ui-combobox__list" role="listbox" id={listId}>
          {loading ? (
            <li className="ui-combobox__status">{loadingLabel}</li>
          ) : options.length === 0 ? (
            <li className="ui-combobox__status">{emptyLabel}</li>
          ) : (
            options.map((option, index) => (
              <li
                key={option.value}
                role="option"
                aria-selected={index === highlight}
                className={cn('ui-combobox__option', index === highlight && 'ui-combobox__option--active')}
                onMouseDown={(event) => { event.preventDefault(); select(option) }}
                onMouseEnter={() => setHighlight(index)}
              >
                <span>{option.label}</span>
                {option.description ? <small>{option.description}</small> : null}
              </li>
            ))
          )}
        </ul>
      ) : null}
    </div>
  )
}
