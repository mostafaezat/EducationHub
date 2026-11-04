import{a as z,b as H}from"./chunk-RBVOBHA5.js";import{a as q}from"./chunk-S4HLP4EF.js";import"./chunk-GNIBQGCV.js";import"./chunk-7UZYWCAJ.js";import"./chunk-JU7FATX5.js";import{Aa as V,Ba as $,Ca as c,Ia as G,l as y,va as h,ya as L}from"./chunk-33RU7VU5.js";import"./chunk-4O3FVBGX.js";import{$ as N,$a as O,Ab as R,Db as u,Eb as m,Fb as v,Gb as j,Lb as W,Na as g,Oa as F,P as D,Q as I,Ra as T,S as E,Sa as B,U as p,Za as k,_a as w,ab as A,bb as b,cb as S,db as l,eb as s,fb as a,ga as f,gb as d,la as C,m as M,sb as _,y as P,ya as o}from"./chunk-IGEGARMY.js";var J=`
    .p-progressspinner {
        position: relative;
        margin: 0 auto;
        width: 100px;
        height: 100px;
        display: inline-block;
    }

    .p-progressspinner::before {
        content: '';
        display: block;
        padding-top: 100%;
    }

    .p-progressspinner-spin {
        height: 100%;
        transform-origin: center center;
        width: 100%;
        position: absolute;
        top: 0;
        bottom: 0;
        left: 0;
        right: 0;
        margin: auto;
        animation: p-progressspinner-rotate 2s linear infinite;
    }

    .p-progressspinner-circle {
        stroke-dasharray: 89, 200;
        stroke-dashoffset: 0;
        stroke: dt('progressspinner.colorOne');
        animation:
            p-progressspinner-dash 1.5s ease-in-out infinite,
            p-progressspinner-color 6s ease-in-out infinite;
        stroke-linecap: round;
    }

    @keyframes p-progressspinner-rotate {
        100% {
            transform: rotate(360deg);
        }
    }
    @keyframes p-progressspinner-dash {
        0% {
            stroke-dasharray: 1, 200;
            stroke-dashoffset: 0;
        }
        50% {
            stroke-dasharray: 89, 200;
            stroke-dashoffset: -35px;
        }
        100% {
            stroke-dasharray: 89, 200;
            stroke-dashoffset: -124px;
        }
    }
    @keyframes p-progressspinner-color {
        100%,
        0% {
            stroke: dt('progressspinner.color.one');
        }
        40% {
            stroke: dt('progressspinner.color.two');
        }
        66% {
            stroke: dt('progressspinner.color.three');
        }
        80%,
        90% {
            stroke: dt('progressspinner.color.four');
        }
    }
`;var Z={root:()=>["p-progressspinner"],spin:"p-progressspinner-spin",circle:"p-progressspinner-circle"},K=(()=>{class e extends L{name="progressspinner";style=J;classes=Z;static \u0275fac=(()=>{let n;return function(t){return(n||(n=C(e)))(t||e)}})();static \u0275prov=D({token:e,factory:e.\u0275fac})}return e})();var Q=new E("PROGRESSSPINNER_INSTANCE"),x=(()=>{class e extends ${$pcProgressSpinner=p(Q,{optional:!0,skipSelf:!0})??void 0;bindDirectiveInstance=p(c,{self:!0});styleClass;strokeWidth="2";fill="none";animationDuration="2s";ariaLabel;onAfterViewChecked(){this.bindDirectiveInstance.setAttrs(this.ptms(["host","root"]))}_componentStyle=p(K);static \u0275fac=(()=>{let n;return function(t){return(n||(n=C(e)))(t||e)}})();static \u0275cmp=g({type:e,selectors:[["p-progressSpinner"],["p-progress-spinner"],["p-progressspinner"]],hostVars:5,hostBindings:function(i,t){i&2&&(k("aria-label",t.ariaLabel)("role","progressbar")("aria-busy",!0),u(t.cn(t.cx("root"),t.styleClass)))},inputs:{styleClass:"styleClass",strokeWidth:"strokeWidth",fill:"fill",animationDuration:"animationDuration",ariaLabel:"ariaLabel"},features:[W([K,{provide:Q,useExisting:e},{provide:V,useExisting:e}]),B([c]),T],decls:2,vars:10,consts:[["viewBox","25 25 50 50",3,"pBind"],["cx","50","cy","50","r","20","stroke-miterlimit","10",3,"pBind"]],template:function(i,t){i&1&&(N(),s(0,"svg",0),d(1,"circle",1),a()),i&2&&(u(t.cx("spin")),R("animation-duration",t.animationDuration),l("pBind",t.ptm("spin")),o(),u(t.cx("circle")),l("pBind",t.ptm("circle")),k("fill",t.fill)("stroke-width",t.strokeWidth))},dependencies:[y,h,c],encapsulation:2,changeDetection:0})}return e})(),U=(()=>{class e{static \u0275fac=function(i){return new(i||e)};static \u0275mod=F({type:e});static \u0275inj=I({imports:[x,h,h]})}return e})();var ne=(e,r)=>r.area;function te(e,r){e&1&&(s(0,"div",2),d(1,"p-progressSpinner",5),a())}function ie(e,r){if(e&1&&d(0,"p-message",3),e&2){let n=_();l("text",n.error())}}function re(e,r){if(e&1&&(s(0,"span",8),m(1),a()),e&2){let n=r.$implicit;o(),v(n)}}function oe(e,r){if(e&1&&(s(0,"div",6)(1,"h4"),m(2),a(),s(3,"div",7),b(4,re,2,1,"span",8,A),a()()),e&2){let n=r.$implicit;o(2),v(n.area),o(2),S(n.permissions)}}function se(e,r){if(e&1&&(s(0,"div",4),b(1,oe,6,1,"div",6,ne),a()),e&2){let n=_();o(),S(n.areas())}}var X=class e{t=p(G).t;roles=p(q);areas=f([]);loading=f(!0);error=f(null);ngOnInit(){this.roles.catalog().pipe(P(()=>M([]))).subscribe({next:r=>{this.areas.set(r),this.loading.set(!1)},error:()=>{this.error.set(this.t("permissions.loadFailed")),this.loading.set(!1)}})}static \u0275fac=function(n){return new(n||e)};static \u0275cmp=g({type:e,selectors:[["app-permissions"]],decls:9,vars:3,consts:[[1,"page-header"],[1,"subtitle"],[1,"state-panel"],["severity","error",3,"text"],[1,"areas"],["strokeWidth","4"],[1,"card","perm-area"],[1,"perm-list"],[1,"perm-chip"]],template:function(n,i){n&1&&(s(0,"div",0)(1,"div")(2,"h1"),m(3),a(),s(4,"div",1),m(5),a()()(),w(6,te,2,0,"div",2)(7,ie,1,1,"p-message",3)(8,se,3,0,"div",4)),n&2&&(o(3),v(i.t("nav.permissionsCatalog")),o(2),j(" ",i.t("permissions.subtitle")," "),o(),O(i.loading()?6:i.error()?7:8))},dependencies:[y,H,z,U,x],styles:[".areas[_ngcontent-%COMP%]{display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:1rem}.perm-area[_ngcontent-%COMP%]   h4[_ngcontent-%COMP%]{margin:0 0 .6rem;font-size:.95rem}.perm-chip[_ngcontent-%COMP%]{display:inline-block;background:#eef4ff;color:#1d4ed8;border-radius:6px;padding:.15rem .5rem;font-size:.78rem;margin:.15rem}"]})};export{X as PermissionsComponent};
