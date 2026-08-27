import { Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

type AuthMode = 'login' | 'register';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthService);

  protected readonly authError = this.auth.authError;
  protected readonly authenticating = this.auth.isAuthenticating;
  protected readonly forgotPasswordNotice = signal<string | null>(null);

  protected readonly mode = signal<AuthMode>('login');
  // The real backend (RegisterRequest handling in AuthEndpointExtensions.cs)
  // enforces an 8-character minimum on new accounts; login only sanity-checks
  // client-side since the real check is the password hash comparison.
  protected readonly passwordMinLength = computed(() => (this.mode() === 'register' ? 8 : 6));

  protected readonly form = this.fb.group({
    email: this.fb.control('', [Validators.required, Validators.email]),
    password: this.fb.control('', [Validators.required, Validators.minLength(6)]),
    rememberMe: this.fb.control(true),
  });

  protected isInvalid(controlName: 'email' | 'password'): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && (control.dirty || control.touched);
  }

  protected switchMode(mode: AuthMode): void {
    this.mode.set(mode);
    this.auth.authError.set(null);

    const passwordControl = this.form.controls.password;
    passwordControl.setValidators([Validators.required, Validators.minLength(this.passwordMinLength())]);
    passwordControl.updateValueAndValidity();
  }

  protected onForgotPassword(): void {
    this.forgotPasswordNotice.set(
      'Password reset is not available yet — the backend has no reset endpoint.',
    );
  }

  protected onSubmit(): void {
    if (this.authenticating()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { email, password, rememberMe } = this.form.getRawValue();
    void this.submit(email, password, rememberMe);
  }

  private async submit(email: string, password: string, rememberMe: boolean): Promise<void> {
    if (this.mode() === 'register') {
      await this.auth.register(email, password);
    } else {
      await this.auth.login(email, password, rememberMe);
    }

    if (this.auth.isAuthenticated()) {
      await this.router.navigateByUrl('/search');
    }
  }
}
