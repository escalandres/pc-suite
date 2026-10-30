import { forwardRef } from 'react';
import type { SVGProps } from 'react';

/**
 * Icono personalizado para el menú "Limpiador" (Task 011).
 * SVG: lucide-brush-cleaning — adaptado para aceptar `size` y `className`
 * como el resto de iconos de lucide-react usados en la sidebar.
 */
export const CleanerIcon = forwardRef<SVGSVGElement, SVGProps<SVGSVGElement> & { size?: number | string; strokeWidth?: number | string; color?: string }>(
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
      <path d="m16 22-1-4" />
      <path d="M19 14a1 1 0 0 0 1-1v-1a2 2 0 0 0-2-2h-3a1 1 0 0 1-1-1V4a2 2 0 0 0-4 0v5a1 1 0 0 1-1 1H6a2 2 0 0 0-2 2v1a1 1 0 0 0 1 1" />
      <path d="M19 14H5l-1.973 6.767A1 1 0 0 0 4 22h16a1 1 0 0 0 .973-1.233z" />
      <path d="m8 22 1-4" />
    </svg>
  ),
);

CleanerIcon.displayName = 'CleanerIcon';
