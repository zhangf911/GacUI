﻿/***********************************************************************
Vczh Library++ 3.0
Developer: Zihan Chen(vczh)
GacUI::VTrackerTemplate

This file is generated by: Vczh GacUI Resource Code Generator
***********************************************************************/

#ifndef VCZH_GACUI_RESOURCE_CODE_GENERATOR_GacStudioUI_VTrackerTemplate
#define VCZH_GACUI_RESOURCE_CODE_GENERATOR_GacStudioUI_VTrackerTemplate

#include "GacStudioUIPartialClasses.h"

namespace darkskin
{
	class VTrackerTemplate : public darkskin::VTrackerTemplate_<darkskin::VTrackerTemplate>
	{
		friend class darkskin::VTrackerTemplate_<darkskin::VTrackerTemplate>;
		friend struct vl::reflection::description::CustomTypeDescriptorSelector<darkskin::VTrackerTemplate>;
	protected:

		Point								draggingStartLocation;
		bool								draggingHandle = false;

		// #region CLASS_MEMBER_GUIEVENT_HANDLER (DO NOT PUT OTHER CONTENT IN THIS #region.)
		void OnHandleMouseDown(GuiGraphicsComposition* sender, vl::presentation::compositions::GuiMouseEventArgs& arguments);
		void OnHandleMouseMove(GuiGraphicsComposition* sender, vl::presentation::compositions::GuiMouseEventArgs& arguments);
		void OnHandleMouseUp(GuiGraphicsComposition* sender, vl::presentation::compositions::GuiMouseEventArgs& arguments);
		// #endregion CLASS_MEMBER_GUIEVENT_HANDLER
	public:
		VTrackerTemplate();
	};
}

#endif
