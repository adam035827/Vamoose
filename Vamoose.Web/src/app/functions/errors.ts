import { HttpErrorResponse } from '@angular/common/http';
import { ProblemDetails } from '@interfaces/api';

// Turns an HTTP failure into a message that is safe and useful to show.
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const problem = error.error as ProblemDetails | null;
    if (problem?.detail) {
      return problem.detail;
    }
    if (error.status === 0) {
      return 'Can’t reach the Vamoose server. Is the API running?';
    }
  }
  return 'Something went wrong. Please try again.';
}
