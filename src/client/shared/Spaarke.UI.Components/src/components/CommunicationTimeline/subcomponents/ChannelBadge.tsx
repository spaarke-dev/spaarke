/**
 * ChannelBadge.tsx
 *
 * Distinguishes Email vs Message (chat) rows in the interleaved timeline
 * (task 060, FR-10). Fluent v9 only — `Badge` semantic `color` intents, no
 * hardcoded colors (ADR-021); passes through dark mode via the host
 * `FluentProvider`.
 */
import * as React from 'react';
import { Badge } from '@fluentui/react-components';
import { MailRegular, ChatRegular } from '@fluentui/react-icons';
import type { TimelineChannelType } from '../CommunicationTimeline.types';

export interface IChannelBadgeProps {
  channelType: TimelineChannelType;
}

export const ChannelBadge: React.FC<IChannelBadgeProps> = ({ channelType }) => {
  const isEmail = channelType === 'email';
  return (
    <Badge
      appearance="tint"
      color={isEmail ? 'informative' : 'success'}
      icon={isEmail ? <MailRegular /> : <ChatRegular />}
      size="small"
    >
      {isEmail ? 'Email' : 'Message'}
    </Badge>
  );
};

ChannelBadge.displayName = 'ChannelBadge';
