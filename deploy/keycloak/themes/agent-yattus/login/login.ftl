<#-- Agent Yattus : le formulaire de connexion de keycloak.v2 26.8.1 (mêmes champs, mêmes noms), avec le balisage de la maquette. -->
<#import "template.ftl" as layout>
<#import "social-providers.ftl" as identityProviders>
<#import "passkeys.ftl" as passkeys>
<@layout.registrationLayout displayMessage=!messagesPerField.existsError('username','password') displayInfo=realm.password && realm.registrationAllowed && !registrationDisabled??; section>
<!-- template: login.ftl -->

    <#if section = "header">
        ${msg("loginAccountTitle")}
    <#elseif section = "form">
        <div id="kc-form">
          <div id="kc-form-wrapper">
            <#if realm.password>
                <#assign loginError = messagesPerField.getFirstError('username','password')>
                <form id="kc-form-login" class="ay-form" onsubmit="login.disabled = true; return true;" action="${url.loginAction}" method="post" novalidate="novalidate">
                    <#if !usernameHidden??>
                        <div class="ay-group">
                            <label for="username" class="ay-label"><#if !realm.loginWithEmailAllowed>${msg("username")}<#elseif !realm.registrationEmailAsUsername>${msg("usernameOrEmail")}<#else>${msg("email")}</#if></label>
                            <div class="ay-field<#if loginError?has_content> ay-field--error</#if>">
                                <span class="ay-icon ay-icon--user" aria-hidden="true"></span>
                                <input id="username" name="username" value="${login.username!''}" type="text" autofocus
                                       autocomplete="${(enableWebAuthnConditionalUI?has_content)?then('username webauthn', 'username')}"
                                       aria-invalid="<#if loginError?has_content>true</#if>"<#if loginError?has_content> aria-describedby="input-error"</#if>/>
                            </div>
                        </div>
                    </#if>

                    <div class="ay-group">
                        <div class="ay-label-row">
                            <label for="password" class="ay-label">${msg("password")}</label>
                            <#if realm.resetPasswordAllowed>
                                <a class="ay-link" href="${url.loginResetCredentialsUrl}">${msg("doForgotPassword")}</a>
                            </#if>
                        </div>
                        <div class="ay-field ay-field--action<#if loginError?has_content> ay-field--error</#if>">
                            <span class="ay-icon ay-icon--lock" aria-hidden="true"></span>
                            <input id="password" name="password" type="password" autocomplete="current-password"<#if usernameHidden??> autofocus</#if>
                                   aria-invalid="<#if loginError?has_content>true</#if>"<#if loginError?has_content> aria-describedby="input-error"</#if>/>
                            <button class="ay-reveal" type="button" id="password-show-password" aria-controls="password" aria-label="${msg('showPassword')}"
                                    data-password-toggle data-icon-show="ay-eye" data-icon-hide="ay-eye ay-eye--off"
                                    data-label-show="${msg('showPassword')}" data-label-hide="${msg('hidePassword')}"><span class="ay-eye" aria-hidden="true"></span></button>
                        </div>
                    </div>

                    <#if loginError?has_content>
                        <p id="input-error" class="ay-error" aria-live="polite">${loginError}</p>
                    </#if>

                    <#if realm.rememberMe && !usernameHidden??>
                        <label class="ay-check" for="rememberMe">
                            <input type="checkbox" id="rememberMe" name="rememberMe"<#if login.rememberMe??> checked</#if>/>
                            ${msg("rememberMe")}
                        </label>
                    </#if>

                    <input type="hidden" id="id-hidden-input" name="credentialId" <#if auth.selectedCredential?has_content>value="${auth.selectedCredential}"</#if>/>
                    <button class="ay-btn" id="kc-login" name="login" type="submit">
                        ${msg("doLogIn")}
                        <span class="ay-arrow" aria-hidden="true"></span>
                    </button>
                </form>
            </#if>
          </div>
        </div>
        <@passkeys.conditionalUIData />
    <#elseif section = "socialProviders" >
        <#if realm.password && social.providers?? && social.providers?has_content>
            <@identityProviders.show social=social/>
        </#if>
    <#elseif section = "info" >
        <#if realm.password && realm.registrationAllowed && !registrationDisabled??>
            <div id="kc-registration-container">
                <div id="kc-registration">
                    <span>${msg("noAccount")} <a href="${url.registrationUrl}">${msg("doRegister")}</a></span>
                </div>
            </div>
        </#if>
    </#if>

</@layout.registrationLayout>
