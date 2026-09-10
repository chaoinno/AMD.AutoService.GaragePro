import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Image as ImageIcon, Send, X } from 'lucide-react'
import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { toast } from 'sonner'
import { uploadAttachment } from '../../../api/attachments'
import { isApiError } from '../../../api/client'
import { sendJobChatMessage } from '../../../api/jobChat'
import { getStaffs } from '../../../api/staffs'
import type { JobChatMessage } from '../../../api/types'
import { buildMentionToken, extractMentionedStaffIds, findMentionTriggerIndex } from './mentionToken'

type PendingImage = { file: File; previewUrl: string }

export function JobChatComposer({
  jobId,
  replyTo,
  onClearReply,
  onSent,
}: {
  jobId: string
  replyTo: JobChatMessage | null
  onClearReply: () => void
  onSent: () => void
}) {
  const queryClient = useQueryClient()
  const [body, setBody] = useState('')
  const [images, setImages] = useState<PendingImage[]>([])
  const [mentionAnchor, setMentionAnchor] = useState<number | null>(null)
  const [mentionQuery, setMentionQuery] = useState('')
  const [highlight, setHighlight] = useState(0)
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const mentionOpen = mentionAnchor !== null

  const staffQuery = useQuery({
    queryKey: ['staffs', 'chat-mention', mentionQuery],
    queryFn: () => getStaffs({ keyword: mentionQuery, pageSize: 8 }),
    enabled: mentionOpen,
  })
  const staffOptions = staffQuery.data?.items ?? []

  useEffect(() => { setHighlight(0) }, [staffOptions.length])

  useEffect(() => () => {
    images.forEach((image) => URL.revokeObjectURL(image.previewUrl))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const sendMutation = useMutation({
    mutationFn: async () => {
      const attachmentIds: string[] = []
      for (const image of images) {
        const uploaded = await uploadAttachment({ jobId, kind: 'chat', file: image.file })
        attachmentIds.push(uploaded.id)
      }
      return sendJobChatMessage(jobId, {
        body: body.trim() || null,
        replyToMessageId: replyTo?.id ?? null,
        mentionedStaffIds: extractMentionedStaffIds(body),
        attachmentIds,
      })
    },
    onSuccess: () => {
      images.forEach((image) => URL.revokeObjectURL(image.previewUrl))
      setBody('')
      setImages([])
      onClearReply()
      onSent()
      void queryClient.invalidateQueries({ queryKey: ['job-chat-peek', jobId] })
    },
    onError: (error) => {
      toast.error(isApiError(error) ? error.messageTh : 'ส่งข้อความไม่สำเร็จ')
    },
  })

  const canSend = (body.trim().length > 0 || images.length > 0) && !sendMutation.isPending

  const onChangeBody = (value: string, caret: number) => {
    setBody(value)
    const anchor = findMentionTriggerIndex(value, caret)
    setMentionAnchor(anchor)
    setMentionQuery(anchor === null ? '' : value.slice(anchor + 1, caret))
  }

  const selectMention = (staffId: number, fullName: string) => {
    if (mentionAnchor === null || !textareaRef.current) return
    const caret = textareaRef.current.selectionStart ?? body.length
    const token = buildMentionToken(staffId, fullName)
    const next = `${body.slice(0, mentionAnchor)}${token} ${body.slice(caret)}`
    setBody(next)
    setMentionAnchor(null)
    setMentionQuery('')
    const nextCaret = mentionAnchor + token.length + 1
    requestAnimationFrame(() => {
      textareaRef.current?.focus()
      textareaRef.current?.setSelectionRange(nextCaret, nextCaret)
    })
  }

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (mentionOpen && staffOptions.length > 0) {
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        setHighlight((h) => Math.min(h + 1, staffOptions.length - 1))
        return
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault()
        setHighlight((h) => Math.max(h - 1, 0))
        return
      }
      if (event.key === 'Enter') {
        event.preventDefault()
        const picked = staffOptions[highlight]
        if (picked) selectMention(picked.id, picked.fullName)
        return
      }
      if (event.key === 'Escape') {
        event.preventDefault()
        setMentionAnchor(null)
        setMentionQuery('')
        return
      }
    }

    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault()
      if (canSend) sendMutation.mutate()
    }
  }

  const onPickImages = (files: FileList | null) => {
    if (!files || files.length === 0) return
    const next = Array.from(files).map((file) => ({ file, previewUrl: URL.createObjectURL(file) }))
    setImages((prev) => [...prev, ...next])
    if (fileInputRef.current) fileInputRef.current.value = ''
  }

  const removeImage = (index: number) => {
    setImages((prev) => {
      const target = prev[index]
      if (target) URL.revokeObjectURL(target.previewUrl)
      return prev.filter((_, i) => i !== index)
    })
  }

  return (
    <div className="job-chat-composer">
      {replyTo ? (
        <div className="job-chat-composer__reply">
          <div className="job-chat-composer__reply-text">
            <strong>ตอบกลับ {replyTo.createdByUserName}</strong>
            <span>{replyTo.isDeleted ? 'ข้อความนี้ถูกลบ' : (replyTo.body || 'รูปภาพ')}</span>
          </div>
          <button type="button" onClick={onClearReply} aria-label="ยกเลิกการตอบกลับ">
            <X className="job-chat-composer__reply-close" aria-hidden="true" />
          </button>
        </div>
      ) : null}

      {images.length > 0 ? (
        <div className="job-chat-composer__images">
          {images.map((image, index) => (
            <div key={image.previewUrl} className="job-chat-composer__image">
              <img src={image.previewUrl} alt="รูปที่แนบ" />
              <button type="button" onClick={() => removeImage(index)} aria-label="ลบรูปนี้">
                <X className="job-chat-composer__image-remove" aria-hidden="true" />
              </button>
            </div>
          ))}
        </div>
      ) : null}

      <div className="job-chat-composer__input-row">
        <div className="job-chat-composer__textarea-wrap">
          {mentionOpen ? (
            <ul className="job-chat-mention-list" role="listbox">
              {staffQuery.isFetching ? (
                <li className="job-chat-mention-list__status">กำลังค้นหา…</li>
              ) : staffOptions.length === 0 ? (
                <li className="job-chat-mention-list__status">ไม่พบพนักงานที่ค้นหา</li>
              ) : (
                staffOptions.map((staff, index) => (
                  <li
                    key={staff.id}
                    role="option"
                    aria-selected={index === highlight}
                    className={`job-chat-mention-list__option ${index === highlight ? 'job-chat-mention-list__option--active' : ''}`}
                    onMouseDown={(event) => { event.preventDefault(); selectMention(staff.id, staff.fullName) }}
                    onMouseEnter={() => setHighlight(index)}
                  >
                    {staff.code} · {staff.fullName}
                  </li>
                ))
              )}
            </ul>
          ) : null}
          <textarea
            ref={textareaRef}
            className="job-chat-composer__textarea"
            placeholder="พิมพ์ข้อความ… (@ เพื่อกล่าวถึงพนักงาน)"
            rows={2}
            value={body}
            onChange={(event) => onChangeBody(event.target.value, event.target.selectionStart ?? event.target.value.length)}
            onKeyDown={onKeyDown}
          />
        </div>

        <input
          ref={fileInputRef}
          type="file"
          accept="image/png,image/jpeg,image/webp"
          multiple
          hidden
          onChange={(event) => onPickImages(event.target.files)}
        />
        <button
          type="button"
          className="job-chat-composer__icon-button"
          onClick={() => fileInputRef.current?.click()}
          aria-label="แนบรูปภาพ"
        >
          <ImageIcon aria-hidden="true" />
        </button>
        <button
          type="button"
          className="job-chat-composer__send"
          disabled={!canSend}
          title={canSend ? undefined : 'พิมพ์ข้อความหรือแนบรูปก่อนส่ง'}
          onClick={() => sendMutation.mutate()}
        >
          <Send aria-hidden="true" />
        </button>
      </div>
    </div>
  )
}
