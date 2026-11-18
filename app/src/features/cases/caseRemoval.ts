import { canTransition, type CaseStatus } from '../../types/domain';

/** Removal is only open until the check is graded; the lifecycle decides that, not this page. */
export function canRemoveCase(status: CaseStatus | null | undefined): boolean {
  return Boolean(status) && status !== 'No Check Required' && canTransition(status, 'No Check Required');
}
