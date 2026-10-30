import { forwardRef } from 'react';
import type { SVGProps } from 'react';

/**
 * Icono personalizado para el menú "IA" (Task 038).
 * SVG: lucide-cpu-ai — adaptado para aceptar `size`, `strokeWidth`, `color` y
 * `className` como el resto de iconos de lucide-react usados en la sidebar.
 */
export const AICPUIcon = forwardRef<SVGSVGElement, SVGProps<SVGSVGElement> & { size?: number | string; strokeWidth?: number | string; color?: string }>(
  ({ size = 16, strokeWidth = 2, color = 'currentColor', className, ...props }, ref) => (
    <svg
      ref={ref}
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke={color}
      strokeWidth={strokeWidth}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden
      {...props}
    >
      <path d="M12 20v2" />
      <path d="M12 2v2" />
      <path d="M16 9v6" />
      <path d="M17 20v2" />
      <path d="M17 2v2" />
      <path d="M2 12h2" />
      <path d="M2 17h2" />
      <path d="M2 7h2" />
      <path d="M20 12h2" />
      <path d="M20 17h2" />
      <path d="M20 7h2" />
      <path d="M7 20v2" />
      <path d="M7 2v2" />
      <path d="M8 13h4" />
      <path d="M8 15v-4a2 2 0 014 0v4" />
      <rect x="4" y="4" width="16" height="16" rx="2" />
    </svg>
  ),
);

AICPUIcon.displayName = 'AICPUIcon';
