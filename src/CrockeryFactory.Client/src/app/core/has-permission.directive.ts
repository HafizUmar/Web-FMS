import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { AuthService } from './auth.service';
import { Permission } from './api.types';

/**
 * Structural directive: renders its content only when the user holds the permission.
 *
 *   <button *appHasPermission="'CanManageCatalogue'">New product</button>
 *
 * Reads the same permissions array the server generated from its own policy map, so the
 * client cannot offer an action the server will refuse.
 */
@Directive({ selector: '[appHasPermission]' })
export class HasPermissionDirective {
  private readonly auth = inject(AuthService);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);

  readonly appHasPermission = input.required<Permission>();

  private rendered = false;

  constructor() {
    effect(() => {
      // Touch the user signal so this re-evaluates on sign-in and sign-out.
      this.auth.user();

      const allowed = this.auth.has(this.appHasPermission());

      if (allowed && !this.rendered) {
        this.container.createEmbeddedView(this.template);
        this.rendered = true;
      } else if (!allowed && this.rendered) {
        this.container.clear();
        this.rendered = false;
      }
    });
  }
}
