import * as React from "react";
import { useNavigate } from "react-router-dom";
import {
  makeStyles,
  tokens,
  Text,
  Switch,
  Divider,
  Button,
  Label,
  Field,
  Input,
} from "@fluentui/react-components";
import { ArrowLeftRegular, Person24Regular } from "@fluentui/react-icons";
import { PageContainer } from "../components/PageContainer";
import { SectionCard } from "../components/SectionCard";
import type { PortalUser } from "../types";

const useStyles = makeStyles({
  backRow: {
    display: "flex",
    alignItems: "center",
    marginBottom: tokens.spacingVerticalM,
  },
  avatar: {
    width: "56px",
    height: "56px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    color: tokens.colorNeutralForegroundInverted,
    flexShrink: 0,
  },
  profileRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalL,
    paddingBottom: tokens.spacingVerticalM,
  },
  profileInfo: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },
  settingRow: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
  settingLabel: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
  },
  fieldGrid: {
    display: "grid",
    gridTemplateColumns: "1fr 1fr",
    gap: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    "@media (max-width: 600px)": {
      gridTemplateColumns: "1fr",
    },
  },
});

interface SettingsPageProps {
  isDark: boolean;
  onToggleDark: () => void;
  portalUser: PortalUser | null;
}

/**
 * SettingsPage — user account settings for the Secure External Workspace.
 *
 * Displays:
 * - User profile information (name, email from Entra B2B account)
 * - Appearance settings: light/dark mode toggle
 *
 * Accessible via the user name button in AppHeader → #/settings.
 */
export const SettingsPage: React.FC<SettingsPageProps> = ({
  isDark,
  onToggleDark,
  portalUser,
}) => {
  const styles = useStyles();
  const navigate = useNavigate();

  const initialsFromName = `${portalUser?.firstName?.[0] ?? ""}${portalUser?.lastName?.[0] ?? ""}`.toUpperCase();
  const initials = portalUser
    ? (initialsFromName || portalUser.displayName?.[0]?.toUpperCase() || "?")
    : "?";

  return (
    <PageContainer>
      <div className={styles.backRow}>
        <Button
          appearance="subtle"
          icon={<ArrowLeftRegular />}
          onClick={() => navigate(-1)}
        >
          Back
        </Button>
      </div>

      <Text size={700} weight="semibold" as="h1">
        Account Settings
      </Text>

      {/* Profile card */}
      <SectionCard title="Profile">
        <div className={styles.profileRow}>
          <div className={styles.avatar} aria-hidden="true">
            {portalUser ? (
              <Text size={500} weight="semibold" style={{ color: "inherit" }}>
                {initials}
              </Text>
            ) : (
              <Person24Regular />
            )}
          </div>
          <div className={styles.profileInfo}>
            <Text size={500} weight="semibold">
              {portalUser?.displayName ?? "—"}
            </Text>
            <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
              {portalUser?.userName ?? "—"}
            </Text>
            <Text size={200} style={{ color: tokens.colorNeutralForeground4 }}>
              External user · Entra B2B
            </Text>
          </div>
        </div>

        <Divider />

        <div className={styles.fieldGrid} style={{ marginTop: tokens.spacingVerticalM }}>
          <Field label="First name">
            <Input value={portalUser?.firstName ?? ""} readOnly appearance="outline" />
          </Field>
          <Field label="Last name">
            <Input value={portalUser?.lastName ?? ""} readOnly appearance="outline" />
          </Field>
          <Field label="Email address" style={{ gridColumn: "1 / -1" }}>
            <Input value={portalUser?.userName ?? ""} readOnly appearance="outline" type="email" />
          </Field>
        </div>

        <Text
          size={200}
          style={{ color: tokens.colorNeutralForeground3, marginTop: tokens.spacingVerticalS, display: "block" }}
        >
          Contact information is managed by your Entra identity. To update your profile,
          contact your system administrator.
        </Text>
      </SectionCard>

      {/* Appearance */}
      <SectionCard title="Appearance">
        <div className={styles.settingRow}>
          <div className={styles.settingLabel}>
            <Label size="medium" weight="semibold">
              Dark mode
            </Label>
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              {isDark
                ? "Currently using dark theme"
                : "Currently using light theme — switches automatically with your system preference"}
            </Text>
          </div>
          <Switch
            checked={isDark}
            onChange={(_ev, data) => {
              if (data.checked !== isDark) onToggleDark();
            }}
            label={isDark ? "On" : "Off"}
            labelPosition="before"
          />
        </div>
      </SectionCard>
    </PageContainer>
  );
};

export default SettingsPage;
