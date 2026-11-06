<#-- Agent Yattus : en-tête repris de keycloak.v2 26.8.1 (scripts, mode sombre) ; corps en deux colonnes, visuel à gauche, formulaire à droite. -->
<#import "field.ftl" as field>
<#import "footer.ftl" as loginFooter>
<#import "theme-resources.ftl" as themeResourceTags>
<#macro username>
  <#assign label>
    <#if !realm.loginWithEmailAllowed>${msg("username")}<#elseif !realm.registrationEmailAsUsername>${msg("usernameOrEmail")}<#else>${msg("email")}</#if>
  </#assign>
  <@field.group name="username" label=label>
    <div class="${properties.kcInputGroup}">
      <div class="${properties.kcInputGroupItemClass} ${properties.kcFill}">
        <span class="${properties.kcInputClass} ${properties.kcFormReadOnlyClass}">
          <input id="kc-attempted-username" value="${auth.attemptedUsername}" readonly>
        </span>
      </div>
      <div class="${properties.kcInputGroupItemClass}">
        <button id="reset-login" class="${properties.kcFormPasswordVisibilityButtonClass} kc-login-tooltip" type="button" 
              aria-label="${msg('restartLoginTooltip')}" onclick="location.href='${url.loginRestartFlowUrl}'">
            <i class="fa-sync-alt fas" aria-hidden="true"></i>
            <span class="kc-tooltip-text">${msg("restartLoginTooltip")}</span>
        </button>
      </div>
    </div>
  </@field.group>
</#macro>

<#macro registrationLayout bodyClass="" displayInfo=false displayMessage=true displayRequiredFields=false>
<!DOCTYPE html>
<html class="${properties.kcHtmlClass!}" lang="${lang}"<#if realm.internationalizationEnabled> dir="${(locale.rtl)?then('rtl','ltr')}"</#if>>

<head>
    <meta charset="utf-8">
    <meta http-equiv="Content-Type" content="text/html; charset=UTF-8" />
    <meta name="color-scheme" content="light${darkMode?then(' dark', '')}">
    <meta name="viewport" content="width=device-width, initial-scale=1">

    <#if properties.meta?has_content>
        <#list properties.meta?split(' ') as meta>
            <meta name="${meta?split('==')[0]}" content="${meta?split('==')[1]}"/>
        </#list>
    </#if>
    <title>${title!}</title>
    <#if themeResources?? && themeResources.favicons?has_content>
        <@themeResourceTags.renderFavicons themeResources.favicons url.resourcesPath />
    <#else>
        <link rel="icon" href="${url.resourcesPath}/img/favicon.ico" />
    </#if>
    <#if themeResources?? && themeResources.stylesCommon?has_content>
        <@themeResourceTags.renderStyles themeResources.stylesCommon url.resourcesCommonPath />
    <#elseif properties.stylesCommon?has_content>
        <#list properties.stylesCommon?split(' ') as style>
            <link href="${url.resourcesCommonPath}/${style}" rel="stylesheet" />
        </#list>
    </#if>
    <#if themeResources?? && themeResources.styles?has_content>
        <@themeResourceTags.renderStyles themeResources.styles url.resourcesPath />
    <#elseif properties.styles?has_content>
        <#list properties.styles?split(' ') as style>
            <link href="${url.resourcesPath}/${style}" rel="stylesheet" />
        </#list>
    </#if>
    <script type="importmap">
        {
            "imports": {
                "rfc4648": "${url.resourcesCommonPath}/vendor/rfc4648/rfc4648.js"
            }
        }
    </script>
    <#if darkMode>
      <script type="module" async blocking="render">
          <#outputformat "JavaScript">
          const DARK_MODE_CLASS = ${properties.kcDarkModeClass?c};
          const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");

          updateDarkMode(mediaQuery.matches);
          mediaQuery.addEventListener("change", (event) => updateDarkMode(event.matches));

          function updateDarkMode(isEnabled) {
            const { classList } = document.documentElement;

            if (isEnabled) {
              classList.add(DARK_MODE_CLASS);
            } else {
              classList.remove(DARK_MODE_CLASS);
            }
          }
          </#outputformat>
      </script>
    </#if>
    <#if themeResources?? && themeResources.scripts?has_content>
        <@themeResourceTags.renderScripts themeResources.scripts url.resourcesPath "text/javascript" />
    <#elseif properties.scripts?has_content>
        <#list properties.scripts?split(' ') as script>
            <script src="${url.resourcesPath}/${script}" type="text/javascript"></script>
        </#list>
    </#if>
    <#if scripts??>
        <#list scripts as script>
            <script src="${script}" type="text/javascript"></script>
        </#list>
    </#if>
    <script type="module" src="${url.resourcesPath}/js/passwordVisibility.js"></script>
    <script type="module">
        <#outputformat "JavaScript">
        import { startSessionPolling } from ${(url.resourcesPath + "/js/authChecker.js")?c};

        startSessionPolling(
            ${url.ssoLoginInOtherTabsUrl?c}
        );
        </#outputformat>
    </script>
    <script type="module">
        document.addEventListener("click", (event) => {
            const link = event.target.closest("a[data-once-link]");

            if (!link) {
                return;
            }

            if (link.getAttribute("aria-disabled") === "true") {
                event.preventDefault();
                return;
            }

            const { disabledClass } = link.dataset;

            if (disabledClass) {
                link.classList.add(...disabledClass.trim().split(/\s+/));
            }

            link.setAttribute("role", "link");
            link.setAttribute("aria-disabled", "true");
        });
    </script>
    <#if authenticationSession??>
        <script type="module">
             <#outputformat "JavaScript">
            import { checkAuthSession } from ${(url.resourcesPath + "/js/authChecker.js")?c};

            checkAuthSession(
                ${authenticationSession.authSessionIdHash?c}
            );
            </#outputformat>
        </script>
    </#if>
    <script>
      // Workaround for https://bugzilla.mozilla.org/show_bug.cgi?id=1404468
      const isFirefox = true;
    </script>
</head>

<body id="keycloak-bg" class="ay-body" data-page-id="login-${pageId}">
<div class="ay-login">

  <#-- Visuel : le réseau d'agents. Décoratif, toujours sombre. -->
  <section class="ay-visual" aria-hidden="true">
    <div class="ay-visual__glow"></div>
    <svg class="ay-graph" viewBox="0 0 800 720" preserveAspectRatio="xMidYMid meet">
      <circle cx="400" cy="360" r="128" fill="none" stroke="#ece6d8" stroke-opacity="0.07"/>
      <circle cx="400" cy="360" r="236" fill="none" stroke="#ece6d8" stroke-opacity="0.05" stroke-dasharray="2 6"/>
      <circle cx="400" cy="360" r="330" fill="none" stroke="#ece6d8" stroke-opacity="0.035"/>

      <line x1="400" y1="360" x2="196" y2="214" stroke="#5e9e92" stroke-opacity="0.35" stroke-width="1.2"/>
      <line class="ay-flow" x1="400" y1="360" x2="196" y2="214" stroke="#5e9e92" stroke-width="1.6"/>
      <line x1="400" y1="360" x2="400" y2="132" stroke="#ece6d8" stroke-opacity="0.18" stroke-width="1.2"/>
      <line x1="400" y1="360" x2="612" y2="222" stroke="#a7a196" stroke-opacity="0.55" stroke-width="1.2" stroke-dasharray="5 6"/>
      <line x1="400" y1="360" x2="228" y2="530" stroke="#5e9e92" stroke-opacity="0.35" stroke-width="1.2"/>
      <line class="ay-flow" x1="400" y1="360" x2="228" y2="530" stroke="#5e9e92" stroke-width="1.6"/>
      <line x1="400" y1="360" x2="586" y2="524" stroke="#d8b86e" stroke-opacity="0.6" stroke-width="1.2"/>
      <line x1="196" y1="214" x2="400" y2="132" stroke="#ece6d8" stroke-opacity="0.07"/>
      <line x1="612" y1="222" x2="400" y2="132" stroke="#ece6d8" stroke-opacity="0.07"/>
      <line x1="228" y1="530" x2="586" y2="524" stroke="#ece6d8" stroke-opacity="0.05"/>

      <circle class="ay-pulse" cx="400" cy="360" r="38" fill="none" stroke="#5e9e92" stroke-width="1.5"/>
      <circle cx="400" cy="360" r="38" fill="#101413" stroke="#5e9e92" stroke-width="1.5"/>
      <path d="M386 344 400 359l14-15M400 359v19" fill="none" stroke="#5e9e92" stroke-width="2.6" stroke-linecap="round"/>
      <circle cx="400" cy="359" r="4" fill="#5e9e92"/>

      <circle cx="196" cy="214" r="15" fill="#101413" stroke="#5e9e92" stroke-width="1.5"/>
      <circle cx="196" cy="214" r="4.5" fill="#5e9e92"/>
      <circle cx="400" cy="132" r="12" fill="#121316" stroke="#ece6d8" stroke-opacity="0.35" stroke-width="1.2"/>
      <circle cx="400" cy="132" r="3.5" fill="#ece6d8" fill-opacity="0.6"/>
      <circle cx="612" cy="222" r="15" fill="none" stroke="#a7a196" stroke-width="1.3" stroke-dasharray="3 3"/>
      <circle cx="228" cy="530" r="13" fill="#101413" stroke="#5e9e92" stroke-width="1.5"/>
      <rect x="222" y="524" width="12" height="12" rx="2" fill="none" stroke="#5e9e92" stroke-width="1.4"/>
      <circle class="ay-pulse" cx="586" cy="524" r="15" fill="none" stroke="#d8b86e" stroke-width="1.2"/>
      <circle cx="586" cy="524" r="15" fill="#15130e" stroke="#d8b86e" stroke-width="1.5"/>
      <circle cx="586" cy="524" r="4.5" fill="#d8b86e"/>

      <g font-family="IBM Plex Mono, monospace" font-size="11" letter-spacing="1.2">
        <text x="196" y="252" text-anchor="middle" fill="#a7a196">ANALYSTE</text>
        <text x="400" y="106" text-anchor="middle" fill="#8a857b">MODÈLE</text>
        <text x="612" y="260" text-anchor="middle" fill="#a7a196">RÉDACTEUR</text>
        <text x="228" y="566" text-anchor="middle" fill="#a7a196">WORKSPACE</text>
        <text x="586" y="562" text-anchor="middle" fill="#d8b86e">VOUS</text>
      </g>
    </svg>

    <div class="ay-brand">
      <span class="ay-brand__mark">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#5e9e92" stroke-width="2" stroke-linecap="round"><path d="M5 3.5 12 11l7-7.5M12 11v9.5"/><circle cx="12" cy="11" r="2.2" fill="#5e9e92" stroke="none"/></svg>
      </span>
      <span class="ay-brand__name">${realm.displayName!'Agent Yattus'}</span>
    </div>

    <div class="ay-status"><span class="ay-dot ay-blink"></span>${msg("ayStatus")}</div>

    <div class="ay-mission">
      <div class="ay-mission__head">
        <span class="ay-mission__title">${msg("ayMissionTitle")}</span>
        <span class="ay-chip">${msg("ayMissionState")}</span>
      </div>
      <ul class="ay-mission__steps">
        <li><span class="ay-dot"></span><span class="ay-grow">list_files</span><span class="ay-who">${msg("ayRoleAnalyst")}</span></li>
        <li><span class="ay-dot"></span><span class="ay-grow">read_file src/Api.cs</span><span class="ay-who">${msg("ayRoleAnalyst")}</span></li>
        <li><span class="ay-dot ay-dot--proposed"></span><span class="ay-grow">handoff</span><span class="ay-who">${msg("ayRoleWriter")}</span></li>
        <li class="ay-wait"><span class="ay-dot ay-blink"></span><span class="ay-grow">write_file REPORT.md</span><span>${msg("ayApproval")}</span></li>
      </ul>
    </div>

    <div class="ay-tagline">
      <p class="ay-tagline__text">${msg("ayTagline1")}<br><span>${msg("ayTagline2")}</span></p>
      <div class="ay-tagline__meta"><span>OIDC · PKCE</span><span>AUDIT</span></div>
    </div>
  </section>

  <#-- Formulaire -->
  <main class="ay-panel">
    <div class="ay-panel__inner">
      <header class="ay-head">
        <span class="ay-eyebrow">${msg("ayEyebrow")}</span>
        <h1 class="ay-title" id="kc-page-title"><#nested "header"></h1>
        <#if pageId == "login.ftl">
          <p class="ay-lede">${msg("ayLede")}</p>
        </#if>
      </header>

      <div class="ay-body-content">
        <#if !(auth?has_content && auth.showUsername() && !auth.showResetCredentials())>
            <#if displayRequiredFields>
                <p class="ay-required"><span class="${properties.kcInputRequiredClass!}">*</span> ${msg("requiredFields")}</p>
            </#if>
        <#else>
            <#if displayRequiredFields>
                <p class="ay-required"><span class="${properties.kcInputRequiredClass!}">*</span> ${msg("requiredFields")}</p>
            </#if>
            <div class="${properties.kcFormClass} ${properties.kcContentWrapperClass}">
              <#nested "show-username">
              <@username />
            </div>
        </#if>

        <#-- App-initiated actions should not see warning messages about the need to complete the action during login. -->
        <#if displayMessage && message?has_content && (message.type != 'warning' || !isAppInitiatedAction??)>
            <div class="ay-alert ay-alert--${message.type}" role="alert">
                <span class="ay-alert__dot" aria-hidden="true"></span>
                <span class="kc-feedback-text">${kcSanitize(message.summary)?no_esc}</span>
            </div>
        </#if>

        <#nested "form">

        <#if auth?has_content && auth.showTryAnotherWayLink()>
          <form id="kc-select-try-another-way-form" action="${url.loginAction}" method="post" novalidate="novalidate">
              <input type="hidden" name="tryAnotherWay" value="on"/>
              <a id="try-another-way" href="javascript:document.forms['kc-select-try-another-way-form'].requestSubmit()"
                  class="${properties.kcButtonSecondaryClass} ${properties.kcButtonBlockClass} ${properties.kcMarginTopClass}">
                    ${msg("doTryAnotherWay")}
              </a>
          </form>
        </#if>

        <#if switchOrganizationEnabled?? && switchOrganizationEnabled>
          <form id="kc-switch-organization-form" action="${url.loginAction}" method="post" novalidate="novalidate">
              <input type="hidden" name="switchOrganization" value="true"/>
              <a id="switch-organization" href="javascript:document.forms['kc-switch-organization-form'].requestSubmit()"
                  class="${properties.kcButtonSecondaryClass} ${properties.kcButtonBlockClass} ${properties.kcMarginTopClass}">
                    ${msg("doSwitchOrganization")}
              </a>
          </form>
        </#if>

        <#nested "socialProviders">

        <#if displayInfo>
            <div id="kc-info" class="ay-info">
                <div id="kc-info-wrapper"><#nested "info"></div>
            </div>
        </#if>
      </div>

      <footer class="ay-foot">
        <span class="ay-foot__icon" aria-hidden="true"></span>
        ${msg("ayFootnote")}
      </footer>
    </div>
  </main>
</div>
</body>

</html>
</#macro>
