import { useEffect, useRef, useState, type KeyboardEvent, type ClipboardEvent, type ChangeEvent } from 'react'
import { distributePastedDigits } from './otp-logic'

export interface OtpInputProps {
  value: string
  onChange: (value: string) => void
  length?: number
  disabled?: boolean
  invalid?: boolean
  autoFocus?: boolean
  className?: string
  onComplete?: (code: string) => void
}

export function OtpInput({
  value = '',
  onChange,
  length = 6,
  disabled = false,
  invalid = false,
  autoFocus = true,
  className = '',
  onComplete,
}: OtpInputProps) {
  const inputRefs = useRef<(HTMLInputElement | null)[]>([])
  const [focusedIndex, setFocusedIndex] = useState<number | null>(autoFocus ? 0 : null)

  const digits = Array.from({ length }, (_, i) => value[i] || '')

  useEffect(() => {
    if (autoFocus && inputRefs.current[0]) {
      inputRefs.current[0]?.focus()
    }
  }, [autoFocus])

  function focusIndex(index: number) {
    const target = Math.max(0, Math.min(length - 1, index))
    inputRefs.current[target]?.focus()
    inputRefs.current[target]?.select()
    setFocusedIndex(target)
  }

  function handleDigitChange(index: number, e: ChangeEvent<HTMLInputElement>) {
    const raw = e.target.value
    const clean = raw.replace(/\D/g, '')

    if (!clean) {
      const next = digits.slice()
      next[index] = ''
      onChange(next.join(''))
      return
    }

    if (clean.length > 1) {
      handlePasted(index, clean)
      return
    }

    const char = clean[clean.length - 1]
    const next = digits.slice()
    next[index] = char
    const nextCode = next.join('')
    onChange(nextCode)

    if (index < length - 1) {
      focusIndex(index + 1)
    }

    if (nextCode.length === length && onComplete) {
      onComplete(nextCode)
    }
  }

  function handleKeyDown(index: number, e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Backspace') {
      if (digits[index]) {
        const next = digits.slice()
        next[index] = ''
        onChange(next.join(''))
      } else if (index > 0) {
        const next = digits.slice()
        next[index - 1] = ''
        onChange(next.join(''))
        focusIndex(index - 1)
      }
      e.preventDefault()
    } else if (e.key === 'ArrowLeft') {
      if (index > 0) {
        e.preventDefault()
        focusIndex(index - 1)
      }
    } else if (e.key === 'ArrowRight') {
      if (index < length - 1) {
        e.preventDefault()
        focusIndex(index + 1)
      }
    }
  }

  function handlePaste(e: ClipboardEvent<HTMLInputElement>) {
    e.preventDefault()
    const pasted = e.clipboardData.getData('text')
    if (!pasted) return
    handlePasted(0, pasted)
  }

  function handlePasted(startIndex: number, pastedText: string) {
    const { code, nextFocus } = distributePastedDigits(digits, startIndex, pastedText, length)
    onChange(code)
    focusIndex(nextFocus)
    if (code.length === length && onComplete) {
      onComplete(code)
    }
  }

  return (
    <div
      className={`grid grid-cols-6 gap-2 sm:gap-2.5 w-full ${className}`}
      role="group"
      aria-label="Nhập mã xác nhận 6 số"
    >
      {Array.from({ length }, (_, index) => {
        const isFocused = focusedIndex === index
        const hasValue = !!digits[index]

        return (
          <input
            key={index}
            ref={(el) => {
              inputRefs.current[index] = el
            }}
            type="text"
            inputMode="numeric"
            pattern="[0-9]*"
            maxLength={1}
            autoComplete={index === 0 ? 'one-time-code' : 'off'}
            disabled={disabled}
            value={digits[index]}
            aria-label={`Số thứ ${index + 1}`}
            aria-invalid={invalid}
            onFocus={() => {
              setFocusedIndex(index)
              inputRefs.current[index]?.select()
            }}
            onBlur={() => {
              if (focusedIndex === index) setFocusedIndex(null)
            }}
            onChange={(e) => handleDigitChange(index, e)}
            onKeyDown={(e) => handleKeyDown(index, e)}
            onPaste={handlePaste}
            className={`
              h-13 sm:h-14 w-full text-center text-2xl font-bold tabular-nums
              rounded-xl bg-white transition-all duration-150 outline-none
              ${
                invalid
                  ? 'border-2 border-red-500 text-red-700 bg-red-50/30'
                  : isFocused
                    ? 'border-2 border-[#1a2f25] ring-2 ring-[#1a2f25]/15 text-[#1a2f25] shadow-xs'
                    : hasValue
                      ? 'border border-stone-300 text-[#1a2f25]'
                      : 'border border-stone-200 text-stone-900 hover:border-stone-300'
              }
              disabled:opacity-50 disabled:cursor-not-allowed
            `}
          />
        )
      })}
    </div>
  )
}
